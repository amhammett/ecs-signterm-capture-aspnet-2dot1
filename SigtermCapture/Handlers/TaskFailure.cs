using Amazon.ECS;
using Amazon.ECS.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace SigtermCapture.Handlers
{
    public static class TaskFailure
    {
        public static void CleanUp()
        {
            Console.WriteLine("CleanUp Called");

            if (HasTaskFailed())
            {
                Console.WriteLine("Container failure detected.");
                PerformAdditionalProcessing();
            }
            else
            {
                Console.WriteLine("Normal container stop event. No action required.");
            }
        }

        public static Boolean HasTaskFailed()
        {
            Console.WriteLine("Investigating Task Failure.");
            Boolean isTaskFailed = false;

            // container instance can query its own metadata. used to get uuid and cluster
            Dictionary<string, string> taskData = GetTaskData();
            if (!taskData.ContainsKey("uuid") || !taskData.ContainsKey("cluster"))
            {
                Console.WriteLine("GetTaskData failed lookup.");
            }
            else
            {
                isTaskFailed = QuerySdkTaskStatus(taskData["cluster"], taskData["uuid"]);
            }

            return isTaskFailed;
        }

        // **Step 1** Gather container's details via Task Metadata Endpoint
        public static Dictionary<string, string> GetTaskData()
        {
            Dictionary<string, string> taskData = new Dictionary<string, string>();
            String metadataUri = Environment.GetEnvironmentVariable("ECS_CONTAINER_METADATA_URI");

            if (!string.IsNullOrEmpty(metadataUri))
            {
                WebClient webClient = new WebClient();
                Stream data = webClient.OpenRead(metadataUri);
                StreamReader reader = new StreamReader(data);

                String metadataRaw = reader.ReadToEnd();
                WriteStatusFile("metadata", metadataRaw);

                data.Close();
                reader.Close();
                webClient.Dispose();

                String taskArn = RegexMatchJsonStringAttribute("com.amazonaws.ecs.task-arn", metadataRaw);
                if (taskArn == null)
                {
                    Console.WriteLine("Unable find find task uuid. Are you running on ECS?");
                    return null;
                }
                taskData["uuid"] = taskArn.Split('/')[1];
                taskData["cluster"] = RegexMatchJsonStringAttribute("com.amazonaws.ecs.cluster", metadataRaw);
            }

            return taskData;
        }

        // **Step 2** Look up the current container's status via AWS SDK
        public static Boolean QuerySdkTaskStatus(string cluster, string uuid)
        {
            Boolean taskSdkStatus = false;
            AmazonECSClient ecs = new AmazonECSClient();
            Task<DescribeTasksResponse> response = ecs.DescribeTasksAsync(
                new DescribeTasksRequest
                {
                    Cluster = cluster,
                    Tasks = new List<string> {
                        uuid
                    }
                }
            );

            // this will run until completion or until task is killed due to timeout
            while (response.Status != TaskStatus.RanToCompletion)
            {
                // failure possible if container doesn't have access to 'ecs.DescribeTasks'
                if (response.Status == TaskStatus.Faulted)
                {
                    Console.WriteLine("Error in getting a response from request.");
                    break;
                }
            }

            if (response.Result.Tasks.Count > 0)
            {
                Amazon.ECS.Model.Task currentTask = response.Result.Tasks[0];
                Console.WriteLine("Task stopped reason: {0}", currentTask.StoppedReason);

                if (!ValidTaskStopReason(currentTask.StoppedReason))
                {
                    taskSdkStatus = true;
                }
                else
                {
                    Console.WriteLine("Task has stopped with valid reason: {0}", currentTask.StoppedReason);
                }
            }

            return taskSdkStatus;
        }

        // **Step 3** Within the returned status, check the stopped reason
        public static Boolean ValidTaskStopReason(string stoppedReason)
        {
            Boolean validReason = false; // assume the worst

            // Scaling activity initiated by (deployment ecs-svc/xxxxxx)
            if (stoppedReason.Contains("Scaling activity initiated"))
            {
                validReason = true;
            }
            // add additional reasons as required

            return validReason;
        }

        // **Step 4** Perform any additional processing
        public static void PerformAdditionalProcessing()
        {
            SaveCommandOuput("ps aux", "process", "Gathering environment variables.");
            SaveCommandOuput("env", "env", "Gathering container environment variables.");

        }

        public static void SaveCommandOuput(string command, string name, string message)
        {
            Console.WriteLine(message);
            String processOutput = ExecuteCommand(command);
            WriteStatusFile(name, processOutput);
        }

        // parsing json turned out to be really painful so falling back on regex
        public static String RegexMatchJsonStringAttribute(String attribute, String input)
        {
            // looking for "<attribute>": "<value>" and saving <value> in groups
            string pattern = string.Concat(@"\""", attribute, @"\"":\s*\""([\w-:/]*)\""");
            string attributeValue = null;

            // Console.WriteLine("Searching for pattern {0}", pattern);
            foreach (Match match in Regex.Matches(input, pattern))
            {
                attributeValue = match.Groups[1].Value;
            }
            return attributeValue;
        }

        public static void WriteStatusFile(String key, String value)
        {
            // for simplicity saving to /data which can be mounted within task definition
            String hostname = Environment.GetEnvironmentVariable("HOSTNAME");
            String statusFile = $"/data/status-{hostname}-{key}.txt";
            Console.WriteLine("Writing '{0}'to disk {1}.", key, statusFile);

            try
            {
                File.WriteAllText(statusFile, value);
                ExecuteCommand($"chmod 777 {statusFile}");
            }
            catch
            {
                Console.WriteLine("Failed to write '{0}'to disk '{1}'", key, statusFile);
            }
        }

        public static string ExecuteCommand(string cmd)
        {
            String escapedArgs = cmd.Replace("\"", "\\\"");

            Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{escapedArgs}\""
                }
            };

            process.Start();
            String output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
    }
}