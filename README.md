# Guan Yu Board Games

[![CI](https://github.com/tianyuguan-dev/board-games/actions/workflows/ci.yml/badge.svg)](https://github.com/tianyuguan-dev/board-games/actions/workflows/ci.yml)

A real-time multiplayer board game platform where players join rooms and play together via WebSocket. Supports **Avalon** (social deduction, 5-10 players) and **BlackJack** (1-7 players).

**Live Demo:** [guantianyu.cc](https://guantianyu.cc)

![Avalon Gameplay](snapshot.png)

## Why I Built This

My friends and I play Avalon regularly, but tracking votes on paper is tedious, worn cards give away hidden roles, and there was no way to keep score across game nights. So I built a digital platform we actually use, with ranked and casual modes.

## Tech Stack

**Backend:** C# / ASP.NET Core 8 / SignalR / Entity Framework Core / PostgreSQL / JWT Auth

**Frontend:** React / Vite

**DevOps:** Docker (multi-stage build) / Docker Compose / AWS EC2 / Cloudflare (DNS + SSL) / GitHub Actions CI

**Testing:** xUnit, Moq, WebApplicationFactory: 390+ tests across unit and integration layers (94% line coverage), run on every push by GitHub Actions

## Technical Challenges & Solutions

### Real-time State Synchronization
Players need to see game updates instantly. I used **SignalR WebSockets** for bidirectional communication — the server pushes per-player game state (each player sees different information based on their role) to all connected clients after every action.

### Complex Game State Machine
Avalon has 6 phases (NightReveal -> TeamProposal -> TeamVote -> Mission -> Assassination -> GameOver), each with different valid actions and transitions. I modeled this as a **state machine in the domain layer** with strict validation — the frontend is a thin rendering layer, all game logic lives server-side to prevent cheating.

### Disconnection & Reconnection
Players frequently close browsers or lose connection mid-game. I implemented a **reconnection system** that:
- Tracks disconnected players with a grace period
- Restores full game state (including vote/mission progress) on rejoin
- Handles race conditions when a player refreshes (old connection still active)
- Detects active rooms on login and offers rejoin instead of creating new rooms

### Role-based Information Asymmetry
In Avalon, different roles see different information (e.g., Merlin sees evil players, Percival sees Merlin and Morgana, evil players see each other except Oberon). The server generates **per-player DTOs** — each client only receives the information their role is allowed to see. This prevents any client-side cheating.

### Concurrent Player Actions
Multiple players vote or play mission cards simultaneously. The backend uses thread-safe state management to handle concurrent SignalR calls, resolving actions only when all expected inputs arrive (e.g., all votes collected -> resolve proposal, all mission cards played -> resolve mission).

## How It's Built

I build this with Claude Code under a spec-first workflow: every feature starts as a written spec, is split into small independently testable tasks, and each change is reviewed by the agent and then by me before it merges. The project rules the agent follows (architecture, concurrency and testing rules) live in [CLAUDE.md](CLAUDE.md), and the workflow itself is encoded as custom commands in [`.claude/commands`](.claude/commands): `/spec`, `/breakdown` and `/review`.

## Project Structure

```
BoardGames/              # ASP.NET Core backend (Hubs, Models, Services, Data)
BoardGames.Web/          # React frontend (Vite)
BoardGames.Tests/        # xUnit unit and integration tests
.github/workflows/       # CI: backend tests + frontend build on every push
.claude/commands/        # Claude Code commands for the spec-first workflow
docs/specs/              # Feature specs
Dockerfile               # Multi-stage build: Node 20 (frontend) -> .NET 8 (backend) -> runtime
docker-compose.yml       # App + PostgreSQL containers
```

## How to Run

```bash
# With Docker (recommended)
docker-compose up -d
# App available at http://localhost:80

# Or manually
cd BoardGames.Web && npm install && npm run build
cd ../BoardGames && dotnet run

# Run tests
dotnet test
```
