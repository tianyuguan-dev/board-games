import { useState, useEffect, useRef } from "react";
import Login from "./components/Login";
import Home from "./components/Home";
import Profile from "./components/Profile";
import Lobby from "./components/Lobby";
import Game from "./components/Game";
import AvalonLobby from "./components/avalon/AvalonLobby";
import AvalonGame from "./components/avalon/AvalonGame";
import Admin from "./components/Admin";
import { createConnection } from "./services/signalr";
import { createAvalonConnection } from "./services/avalonSignalr";
import { logout } from "./services/api";

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
  const [mySeatIndex, setMySeatIndex] = useState(-1);
  const [isHost, setIsHost] = useState(false);
  const [roleConfig, setRoleConfig] = useState([]);
  const [maxRejects, setMaxRejects] = useState(5);
  const [gameInProgress, setGameInProgress] = useState(false);
  const [connState, setConnState] = useState("disconnected");
  const [connRetry, setConnRetry] = useState(0);
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

    let intentionalStop = false;

    const conn = selectedGame === "blackjack"
      ? createConnection()
      : createAvalonConnection();

    conn.on("PlayerJoined", (count) => setPlayerCount(count));
    conn.on("PlayerLeft", (count) => setPlayerCount(count));
    conn.on("RoomUpdate", (data) => {
      setRoomPlayers(data.players);
      setIsHost(data.isHost);
      if (data.mySeatIndex != null) setMySeatIndex(data.mySeatIndex);
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
      setMySeatIndex(-1);
      setIsHost(false);
      setRoleConfig([]);
      setMaxRejects(5);
    });
    conn.on("RoomDisbanded", () => {
      setRoomId(null);
      roomIdRef.current = null;
      sessionStorage.removeItem("roomId");
      setMaxPlayers(0);
      setPlayerCount(0);
      setRoomPlayers([]);
      setMySeatIndex(-1);
      setIsHost(false);
      setRoleConfig([]);
      setMaxRejects(5);
      setGameInProgress(false);
    });

    conn.onreconnecting(() => {
      console.log("SignalR reconnecting...");
      setConnState("reconnecting");
    });

    conn.onreconnected(() => {
      console.log("SignalR reconnected");
      setConnState("connected");
      if (roomIdRef.current) {
        conn.invoke("Rejoin", roomIdRef.current).catch((err) => {
          console.warn("Rejoin failed:", err);
        });
      }
    });

    conn.onclose(() => {
      if (intentionalStop) return;
      console.log("SignalR connection closed, will retry in 5s...");
      setConnState("disconnected");
      setConnection(null);
      connRef.current = null;
      needsRejoinRef.current = !!roomIdRef.current;
      setTimeout(() => {
        if (!intentionalStop) setConnRetry((c) => c + 1);
      }, 5000);
    });

    const handleVisibilityChange = () => {
      if (document.visibilityState !== "visible") return;
      if (conn.state === "Connected") return;
      console.log(`Resumed from background (state: ${conn.state}) - forcing reconnect`);
      setConnRetry((c) => c + 1);
    };
    document.addEventListener("visibilitychange", handleVisibilityChange);

    conn
      .start()
      .then(() => {
        console.log(`SignalR connected (${selectedGame})`);
        connRef.current = conn;
        setConnection(conn);
        setConnState("connected");
      })
      .catch((err) => {
        console.error("SignalR connection failed:", err);
        setConnState("disconnected");
        setTimeout(() => {
          if (!intentionalStop) setConnRetry((c) => c + 1);
        }, 5000);
      });

    return () => {
      intentionalStop = true;
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      conn.stop();
      connRef.current = null;
      setConnection(null);
      setConnState("disconnected");
    };
  }, [token, selectedGame, connRetry]);

  function handleLogin(t, refreshToken, nick) {
    localStorage.setItem("token", t);
    localStorage.setItem("refreshToken", refreshToken);
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
    setMySeatIndex(-1);
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
    logout();
  }

  if (window.location.hash === "#admin") {
    return <Admin />;
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
    return <p style={{ textAlign: "center", marginTop: 60 }}>
      {connState === "reconnecting" ? "Reconnecting..." : "Connecting..."}
    </p>;
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
    return <AvalonGame connection={connection} nickname={nickname} roomId={roomId} maxPlayers={maxPlayers} playerCount={playerCount} roomPlayers={roomPlayers} mySeatIndex={mySeatIndex} isHost={isHost} roleConfig={roleConfig} maxRejects={maxRejects} needsRejoin={needsRejoinRef} gameInProgress={gameInProgress} onLeave={handleLeave} />;
  }

  return null;
}

export default App;
