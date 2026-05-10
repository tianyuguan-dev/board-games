import { useState, useEffect } from "react";
import Login from "./components/Login";
import Lobby from "./components/Lobby";
import Game from "./components/Game";
import { createConnection } from "./services/signalr";

function App() {
  const [token, setToken] = useState(null);
  const [connection, setConnection] = useState(null);
  const [roomId, setRoomId] = useState(null);

  useEffect(() => {
    if (!token) return;

    const conn = createConnection(token);
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

  if (!token) {
    return <Login onLogin={setToken} />;
  }

  if (!connection) {
    return <p>Connecting...</p>;
  }

  if (!roomId) {
    return <Lobby connection={connection} onJoinRoom={setRoomId} />;
  }

  return <Game connection={connection} roomId={roomId} onLeave={() => setRoomId(null)} />;
}

export default App;
