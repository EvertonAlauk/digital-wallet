FROM node:22-bookworm-slim
WORKDIR /app

ENV NEXT_TELEMETRY_DISABLED=1
ENV NODE_ENV=development
# The browser only talks to this server. /bff/* and /api/* are rewritten to the BFF on the internal network.
ENV BFF_URL=http://bff:8080

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

EXPOSE 3000
CMD ["npm", "run", "dev", "--", "-H", "0.0.0.0", "-p", "3000"]
