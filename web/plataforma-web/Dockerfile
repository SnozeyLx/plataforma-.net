FROM node:24-alpine AS dependencies
WORKDIR /app
COPY web/plataforma-web/package.json web/plataforma-web/package-lock.json ./
RUN npm ci

FROM node:24-alpine AS build
WORKDIR /app
COPY --from=dependencies /app/node_modules ./node_modules
COPY web/plataforma-web/ ./
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm run build && npm prune --omit=dev

FROM node:24-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production
ENV NEXT_TELEMETRY_DISABLED=1
ENV HOSTNAME=0.0.0.0
RUN addgroup -S --gid 1001 nodejs && adduser -S --uid 1001 nextjs
COPY --from=build --chown=nextjs:nodejs /app ./
USER nextjs
EXPOSE 3000
CMD ["npm", "run", "start"]
