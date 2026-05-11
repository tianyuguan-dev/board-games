import { useState, useEffect } from "react";
import Login from "./components/Login";
import Home from "./components/Home";
import Profile from "./components/Profile";
import Lobby from "./components/Lobby";
import Game from "./components/Game";
import { createConnection } from "./services/signalr";

function App() {
  const [token, setToken] = useState(localStorage.getItem("token"));
  const [nickname, setNickname] = useState(localStorage.getItem("nickname") || "");
  const [connection, setConnection] = useState(null);
  const [selectedGame, setSelectedGame] = useState(null);
  const [showProfile, setShowProfile] = useState(false);
  const [roomId, setRoomId] = useState(null);
  const [maxPlayers, setMaxPlayers] = useState(0);
  const [playerCount, setPlayerCount] = useState(0);
  const [roomPlayers, setRoomPlayers] = useState([]);
  const [isHost, setIsHost] = useState(false);

  useEffect(() => {
    if (!token) return;

    const conn = createConnection(token);
    conn.on("PlayerJoined", (count) => setPlayerCount(count));
    conn.on("PlayerLeft", (count) => setPlayerCount(count));
    conn.on("RoomUpdate", (data) => {
      setRoomPlayers(data.players);
      setIsHost(data.isHost);
    });
    conn.on("Kicked", (reason) => {
      if (reason) alert(reason);
      setRoomId(null);
      setMaxPlayers(0);
      setPlayerCount(0);
      setRoomPlayers([]);
      setIsHost(false);
    });
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

  function handleLogin(t, nick) {
    localStorage.setItem("token", t);
    localStorage.setItem("nickname", nick || "");
    setToken(t);
    setNickname(nick || "");
  }

  function handleNicknameChange(nick) {
    localStorage.setItem("nickname", nick);
    setNickname(nick);
  }

  function handleJoinRoom(id, max, count) {
    setRoomId(id);
    setMaxPlayers(max);
    setPlayerCount(count);
  }

  function handleLeave() {
    setRoomId(null);
    setMaxPlayers(0);
    setPlayerCount(0);
    setRoomPlayers([]);
    setIsHost(false);
  }

  function handleBackToHome() {
    handleLeave();
    setSelectedGame(null);
    setShowProfile(false);
  }

  function handleLogout() {
    localStorage.removeItem("token");
    localStorage.removeItem("nickname");
    setToken(null);
    setNickname("");
    setConnection(null);
    setRoomId(null);
    setSelectedGame(null);
  }

  if (!token) {
    return <Login onLogin={handleLogin} />;
  }

  if (!connection) {
    return <p>Connecting...</p>;
  }

  if (showProfile) {
    return (
      <Profile
        token={token}
        nickname={nickname}
        onNicknameChange={handleNicknameChange}
        onBack={() => setShowProfile(false)}
      />
    );
  }

  if (!selectedGame) {
    return <Home nickname={nickname} onSelectGame={setSelectedGame} onProfile={() => setShowProfile(true)} onLogout={handleLogout} />;
  }

  if (!roomId) {
    return (
      <Lobby
        connection={connection}
        nickname={nickname}
        onJoinRoom={handleJoinRoom}
        onBack={handleBackToHome}
      />
    );
  }

  return (
    <Game
      connection={connection}
      roomId={roomId}
      maxPlayers={maxPlayers}
      playerCount={playerCount}
      roomPlayers={roomPlayers}
      isHost={isHost}
      onLeave={handleLeave}
    />
  );
}

export default App;
