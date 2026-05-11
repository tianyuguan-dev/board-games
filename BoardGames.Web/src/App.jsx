import { useState, useEffect } from "react";
import Login from "./components/Login";
import Lobby from "./components/Lobby";
import Game from "./components/Game";
import { createConnection } from "./services/signalr";

function App() {
  const [token, setToken] = useState(null);
  const [connection, setConnection] = useState(null);
  const [roomId, setRoomId] = useState(null);
  const [maxPlayers, setMaxPlayers] = useState(0);
  const [playerCount, setPlayerCount] = useState(0);

  useEffect(() => {
    if (!token) return;

    const conn = createConnection(token);
    conn.on("PlayerJoined", (count) => setPlayerCount(count));
    conn.on("PlayerLeft", (count) => setPlayerCount(count));
    conn
      .start()
      .then(() => {
        console.log("SignalR connected");
        setConnection(conn);
      })
      .catch((err) => console.error("SignalR connection failed:", err));

    return () => {
      conn.stop();
    };
  }, [token]);

  function handleJoinRoom(id, max, count) {
    setRoomId(id);
    setMaxPlayers(max);
    setPlayerCount(count);
  }

  function handleLeave() {
    setRoomId(null);
    setMaxPlayers(0);
    setPlayerCount(0);
  }

  if (!token) {
    return <Login onLogin={setToken} />;
  }

  if (!connection) {
    return <p>Connecting...</p>;
  }

  if (!roomId) {
    return <Lobby connection={connection} onJoinRoom={handleJoinRoom} />;
  }

  return <Game connection={connection} roomId={roomId} maxPlayers={maxPlayers} playerCount={playerCount} onLeave={handleLeave} />;
}

export default App;
