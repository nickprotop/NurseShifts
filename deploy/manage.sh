#!/bin/bash

# NurseShifts Docker Management Script
# Usage: ./manage.sh [command]

set -e
cd "$(dirname "$0")"

PORT="${PORT:-12000}"

case "$1" in
  start)
    echo "Starting NurseShifts on port $PORT..."
    docker compose up -d
    echo "Running at http://localhost:$PORT"
    ;;
  stop)
    echo "Stopping NurseShifts..."
    docker compose down
    ;;
  restart)
    echo "Restarting NurseShifts..."
    docker compose restart
    ;;
  rebuild)
    echo "Rebuilding and starting NurseShifts..."
    docker compose up -d --build
    echo "Running at http://localhost:$PORT"
    ;;
  logs)
    docker compose logs -f
    ;;
  status)
    docker compose ps
    ;;
  shell)
    echo "Opening shell in container..."
    docker compose exec nurseshifts /bin/bash
    ;;
  clean)
    echo "Stopping and removing containers, networks..."
    docker compose down -v --rmi local
    ;;
  *)
    echo "NurseShifts Docker Management"
    echo ""
    echo "Usage: ./manage.sh [command]"
    echo ""
    echo "Commands:"
    echo "  start    - Start the application"
    echo "  stop     - Stop the application"
    echo "  restart  - Restart the application"
    echo "  rebuild  - Rebuild image and restart"
    echo "  logs     - View container logs (follow)"
    echo "  status   - Show container status"
    echo "  shell    - Open bash shell in container"
    echo "  clean    - Stop and remove everything (including volume!)"
    echo ""
    echo "Environment:"
    echo "  PORT=$PORT (set PORT=xxxx to change)"
    ;;
esac
