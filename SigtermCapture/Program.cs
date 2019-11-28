using System;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using SigtermCapture.Handlers;


namespace SigtermCapture
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var ended = new ManualResetEventSlim();
            var starting = new ManualResetEventSlim();

            AssemblyLoadContext.Default.Unloading += ctx =>
            {
                Console.WriteLine("Unloading fired");
                starting.Set();
                // run clean up on task failure
                TaskFailure.CleanUp();
                Console.WriteLine("Waiting for completion");
                ended.Wait();
            };

            CreateWebHostBuilder(args).Build().Run();

            System.Console.WriteLine("Received signal gracefully shutting down");
            ended.Set();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>();

    }
}
