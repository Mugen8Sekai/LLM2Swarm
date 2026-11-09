#!/usr/bin/env bash
set -euo pipefail
. /etc/os-release
[[ "$VERSION_ID" == "24.04" ]] || { echo 'Expected dedicated Ubuntu 24.04'; exit 1; }
export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y --no-install-recommends git ca-certificates curl python3-venv sudo
mkdir -p /opt/llm2swarm
cd /opt/llm2swarm
if [[ ! -d PX4-Autopilot/.git ]]; then
    git clone --branch v1.17.0 --depth 1 https://github.com/PX4/PX4-Autopilot.git
fi
cd PX4-Autopilot
[[ "$(git describe --tags --exact-match)" == v1.17.0 ]] || { echo 'Unexpected PX4 checkout'; exit 1; }
git submodule update --init --recursive --depth 1
if [[ ! -f /opt/llm2swarm/setup-complete ]]; then
    bash Tools/setup/ubuntu.sh --no-nuttx
    touch /opt/llm2swarm/setup-complete
fi
python3 -m venv /opt/llm2swarm/venv
/opt/llm2swarm/venv/bin/pip install mavsdk==3.17.2
# Bounded compile parallelism. No global WSL resource changes.
make px4_sitl_default -j4
git rev-parse HEAD > /opt/llm2swarm/px4-commit.txt
/opt/llm2swarm/venv/bin/pip freeze > /opt/llm2swarm/python-lock.txt
dpkg-query -W 'gz-*' 'libgz-*' > /opt/llm2swarm/gazebo-packages.txt
echo 'PROVISIONED LLM2Swarm-PX4'
