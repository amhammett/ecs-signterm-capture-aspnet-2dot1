.PHONY: help build run list bash stop inspect auth push dotnet kill guard-%

docker_image_tag := sigterm-capture
docker_containers := docker ps --filter "ancestor=${docker_image_tag}" -q

help: ## this help text
	@echo 'Available targets'
	@grep -E '^[a-zA-Z0-9_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "\033[36m%-30s\033[0m %s\n", $$1, $$2}'

# environment

# development
build: guard-docker_image_tag ## build all the things <docker_image_tag>
	docker build -t ${docker_image_tag} .

run: guard-docker_image_tag ## run all the things <docker_image_tag>
	docker run -it -d --rm -v `pwd`/data:/data -p 8000:80 ${docker_image_tag}

list: ## list all project containers
	@${docker_containers}

bash: ## back into the only container
	docker exec -it `${docker_containers}` bash

stop: ## stop the only container
	docker stop `${docker_containers}`

inspect: ## inspect the only container
	docker inspect `${docker_containers}`

auth: guard-aws_region ## authenticate with ecr <aws_region>
	@echo $$(aws ecr get-login --no-include-email --region ${aws_region})

push: guard-docker_image_tag guard-ecr_arn ## push to ecr <docker_image_tag> <ecr_arn>
	docker build -t ${docker_image_tag} .
	docker tag ${docker_image_tag}:latest ${ecr_arn}:latest
	docker push ${ecr_arn}:latest

dotnet: ## run dotnet manually
	dotnet build SigtermCapture/SigtermCapture.csproj -c Release -o ./bin/build
	dotnet publish SigtermCapture/SigtermCapture.csproj -c Release -o ./bin/publish
	dotnet bin/publish/SigtermCapture.dll

kill: ## kill local project process
	ps aux | awk '/Sigter[m]/{print $$2}' | xargs kill

guard-%: # ensure required vars are set
	@ if [ "${${*}}" = "" ]; then \
		echo "Variable $* not set. Specify via $*=<value>"; \
		exit 1; \
	fi
