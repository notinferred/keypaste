# The offline fixtures start thousands of processes, which Git Bash on Windows does slowly.
# ubuntu-24.04 matches the runner ci.yml's gate job uses for the same scripts.
FROM ubuntu:24.04@sha256:b3cc40b72b93588182b5410f723c7aaf142363311c2aa993d8a453ddcbb3ae15
RUN apt-get update \
  && apt-get install -y --no-install-recommends \
    bsdextrautils ca-certificates curl file git jq openssl python3 xxd zip \
  && rm -rf /var/lib/apt/lists/*
