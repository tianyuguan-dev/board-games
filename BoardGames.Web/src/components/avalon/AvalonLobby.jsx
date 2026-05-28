import { useState, useEffect } from "react";
import "./AvalonGame.css";
import Leaderboard from "../Leaderboard";

export default function AvalonLobby({ connection, nickname, onJoinRoom, onBack }) {
  const [roomId, setRoomId] = useState("");
  const [maxPlayers, setMaxPlayers] = useState(7);
  const [error, setError] = useState("");
  const [balance, setBalance] = useState(null);
  const [leaderboard, setLeaderboard] = useState([]);
  const [activeRoom, setActiveRoom] = useState(null);

  useEffect(() => {
    connection.invoke("GetBalance").then(setBalance).catch(() => {});
    connection.invoke("GetLeaderboard").then(setLeaderboard).catch(() => {});
    connection.invoke("GetActiveRoom").then((id) => setActiveRoom(id || null)).catch(() => {});
  }, [connection]);

  async function handleCreate() {
    try {
      const room = await connection.invoke("CreateRoom", maxPlayers);
      onJoinRoom(room.roomId, room.maxPlayers, 1);
    } catch (e) {
      setError(e.message || "Failed to create room");
    }
  }

  async function handleJoin() {
    try {
      const result = await connection.invoke("JoinRoom", roomId);
      onJoinRoom(roomId, result.maxPlayers, result.playerCount, result.gameInProgress);
    } catch (e) {
      setError(e.message || "Failed to join room");
    }
  }

  async function handleRejoin() {
    try {
      const result = await connection.invoke("Rejoin", activeRoom);
      onJoinRoom(activeRoom, result.maxPlayers, result.playerCount, true);
    } catch {
      try {
        const result = await connection.invoke("JoinRoom", activeRoom);
        onJoinRoom(activeRoom, result.maxPlayers, result.playerCount, result.gameInProgress);
      } catch (e) {
        setError(e.message || "Failed to rejoin");
        setActiveRoom(null);
      }
    }
  }

  return (
    <div className="page-center" style={{ maxWidth: 460 }}>
      <h2>Avalon Lobby</h2>
      {balance !== null && <p className="text-muted" style={{ marginBottom: 12 }}>{nickname || "You"}'s net wins: {balance}</p>}

      {activeRoom ? (
        <div className="section">
          <p>You have an active game in room <strong>{activeRoom}</strong></p>
          <button onClick={handleRejoin}>Rejoin Room {activeRoom}</button>
        </div>
      ) : (
        <>
          <div className="section">
            <h3>Create Room</h3>
            <div className="inline-group">
              <label className="text-muted" style={{ whiteSpace: "nowrap" }}>Players:</label>
              <select value={maxPlayers} onChange={(e) => setMaxPlayers(Number(e.target.value))} style={{ width: 60 }}>
                {[5,6,7,8,9,10].map(n => <option key={n} value={n}>{n}</option>)}
              </select>
              <button onClick={handleCreate}>Create</button>
            </div>
          </div>

          <div className="section">
            <h3>Join Room</h3>
            <div className="inline-group">
              <input
                type="text"
                placeholder="Room ID"
                value={roomId}
                onChange={(e) => setRoomId(e.target.value)}
                style={{ width: 120 }}
              />
              <button onClick={handleJoin}>Join</button>
            </div>
          </div>
        </>
      )}

      {error && <p className="error-msg">{error}</p>}

      {leaderboard.length > 0 && (
        <div className="section">
          <h3>Leaderboard</h3>
          <Leaderboard entries={leaderboard} nickname={nickname} valueLabel="Net Wins" />
        </div>
      )}
      <hr />
      <button onClick={onBack} style={{ background: "#94a3b8" }}>Back</button>
    </div>
  );
}
