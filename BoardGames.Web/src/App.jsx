import { useState, useEffect, useRef } from "react";
import Login from "./components/Login";
import Home from "./components/Home";
import Profile from "./components/Profile";
import Lobby from "./components/Lobby";
import Game from "./components/Game";
import AvalonLobby from "./components/avalon/AvalonLobby";
import AvalonGame from "./components/avalon/AvalonGame";
import { createConnection } from "./services/signalr";
import { createAvalonConnection } from "./services/avalonSignalr";

function App() {
  const [token, setToken] = useState(localStorage.getItem("token"));
  const [nickname, setNickname] = useState(localStorage.getItem("nickname") || "");
  const [connection, setConnection] = useState(null);
  const [selectedGame, setSelectedGame] = useState(sessionStorage.getItem("selectedGame") || null);
  const [showProfile, setShowProfile] = useState(false);
  const [roomId, setRoomId] = useState(sessionStorage.getItem("roomId") || null);
  const [maxPlayers, setMaxPlayers] = useState(0);
  const [playerCount, setPlayerCount] = useState(0);
  const [roomPlayers, setRoomPlayers] = useState([]);
  const [isHost, setIsHost] = useState(false);
  const [roleConfig, setRoleConfig] = useState([]);
  const [maxRejects, setMaxRejects] = useState(5);
  const [gameInProgress, setGameInProgress] = useState(false);
  const connRef = useRef(null);
  const roomIdRef = useRef(sessionStorage.getItem("roomId") || null);
  const needsRejoinRef = useRef(!!sessionStorage.getItem("roomId"));

  // Connect to the correct hub when a game is selected
  useEffect(() => {
    if (!token || !selectedGame) {
      if (connRef.current) {
        connRef.current.stop();
        connRef.current = null;
        setConnection(null);
      }
      return;
    }

    const conn = selectedGame === "blackjack"
      ? createConnection(token)
      : createAvalonConnection(token);

    conn.on("PlayerJoined", (count) => setPlayerCount(count));
    conn.on("PlayerLeft", (count) => setPlayerCount(count));
    conn.on("RoomUpdate", (data) => {
      setRoomPlayers(data.players);
      setIsHost(data.isHost);
      if (data.roleConfig) setRoleConfig(data.roleConfig);
      if (data.maxRejects != null) setMaxRejects(data.maxRejects);
    });
    conn.on("Kicked", (reason) => {
      if (reason) alert(reason);
      setRoomId(null);
      roomIdRef.current = null;
      sessionStorage.removeItem("roomId");
      setMaxPlayers(0);
      setPlayerCount(0);
      setRoomPlayers([]);
      setIsHost(false);
      setRoleConfig([]);
      setMaxRejects(5);
    });

    conn.onreconnected(() => {
      console.log("SignalR reconnected");
      if (roomIdRef.current) {
        conn.invoke("Rejoin", roomIdRef.current).catch((err) => {
          console.warn("Rejoin failed:", err);
        });
      }
    });

    conn
      .start()
      .then(() => {
        console.log(`SignalR connected (${selectedGame})`);
        connRef.current = conn;
        setConnection(conn);
      })
      .catch((err) => console.error("SignalR connection failed:", err));

    return () => {
      conn.stop();
      connRef.current = null;
      setConnection(null);
    };
  }, [token, selectedGame]);

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

  function handleJoinRoom(id, max, count, inProgress) {
    setRoomId(id);
    roomIdRef.current = id;
    needsRejoinRef.current = false;
    sessionStorage.setItem("roomId", id);
    setMaxPlayers(max);
    setPlayerCount(count);
    setGameInProgress(!!inProgress);
  }

  function handleLeave() {
    setRoomId(null);
    roomIdRef.current = null;
    sessionStorage.removeItem("roomId");
    setMaxPlayers(0);
    setPlayerCount(0);
    setRoomPlayers([]);
    setIsHost(false);
    setRoleConfig([]);
    setMaxRejects(5);
    setGameInProgress(false);
  }

  function selectGame(game) {
    setSelectedGame(game);
    if (game) sessionStorage.setItem("selectedGame", game);
    else sessionStorage.removeItem("selectedGame");
  }

  function handleBackToHome() {
    handleLeave();
    selectGame(null);
    setShowProfile(false);
  }

  function handleLogout() {
    localStorage.removeItem("token");
    localStorage.removeItem("nickname");
    sessionStorage.removeItem("selectedGame");
    sessionStorage.removeItem("roomId");
    setToken(null);
    setNickname("");
    setConnection(null);
    setRoomId(null);
    setSelectedGame(null);
  }

  if (!token) {
    return <Login onLogin={handleLogin} />;
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
    return <Home nickname={nickname} onSelectGame={selectGame} onProfile={() => setShowProfile(true)} onLogout={handleLogout} />;
  }

  // Waiting for connection
  if (!connection) {
    return <p style={{ textAlign: "center", marginTop: 60 }}>Connecting...</p>;
  }

  // BlackJack
  if (selectedGame === "blackjack") {
    if (!roomId) {
      return <Lobby connection={connection} nickname={nickname} onJoinRoom={handleJoinRoom} onBack={handleBackToHome} />;
    }
    return <Game connection={connection} roomId={roomId} maxPlayers={maxPlayers} playerCount={playerCount} roomPlayers={roomPlayers} isHost={isHost} onLeave={handleLeave} />;
  }

  // Avalon
  if (selectedGame === "avalon") {
    if (!roomId) {
      return <AvalonLobby connection={connection} nickname={nickname} onJoinRoom={handleJoinRoom} onBack={handleBackToHome} />;
    }
    return <AvalonGame connection={connection} nickname={nickname} roomId={roomId} maxPlayers={maxPlayers} playerCount={playerCount} roomPlayers={roomPlayers} isHost={isHost} roleConfig={roleConfig} maxRejects={maxRejects} needsRejoin={needsRejoinRef} gameInProgress={gameInProgress} onLeave={handleLeave} />;
  }

  return null;
}

export default App;
