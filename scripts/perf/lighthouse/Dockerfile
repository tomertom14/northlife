# Lighthouse in a container, so measurements do not depend on the host's browser, extensions or antivirus
# (a web-protection proxy on the development machine decompressed every response before the browser saw it).
# Build: docker build -t northlife-lighthouse scripts/perf/lighthouse
# Run:   see scripts/perf/lighthouse/run.ps1
FROM node:24-bookworm-slim
RUN apt-get update \
    && apt-get install --yes --no-install-recommends chromium fonts-noto-core \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /work
RUN npm init --yes > /dev/null && npm install --no-audit --no-fund lighthouse@12.8.2 chrome-launcher@1
COPY audit.mjs ./
ENV CHROME_PATH=/usr/bin/chromium
ENTRYPOINT ["node", "audit.mjs"]
