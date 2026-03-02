echo "Starting Docker containers..."
docker compose up -d

echo "Waiting for port 4200..."

until nc -z localhost 4201; do
  sleep 1
done

echo "Opening browser..."
explorer.exe http://localhost:4200