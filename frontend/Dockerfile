# ===== Build Stage =====
FROM node:20-alpine AS builder

WORKDIR /app
COPY package*.json ./
RUN npm install

COPY . .

# Build SSR app
RUN npm run build

# ===== Runtime Stage =====
FROM node:20-alpine

WORKDIR /app

# copy built output
COPY --from=builder /app/dist ./dist
COPY --from=builder /app/package*.json ./

RUN npm install --omit=dev

EXPOSE 4200

CMD ["node", "dist/AvFrontEnd/server/server.mjs"]