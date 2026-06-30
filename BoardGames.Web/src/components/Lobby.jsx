import { useState, useEffect } from "react";
import Leaderboard from "./Leaderboard";
import WheelPicker from "./WheelPicker";

export default function Lobby({ connection, nickname, onJoinRoom, onBack }) {
  const [roomId, setRoomId] = useState("");
  const [maxPlayers, setMaxPlayers] = useState(4);
  const [error, setError] = useState("");
  const [balance, setBalance] = useState(null);
  const [leaderboard, setLeaderboard] = useState([]);

  useEffect(() => {
    connection.invoke("GetBalance").then(setBalance).catch(() => {});
    connection.invoke("GetLeaderboard").then(setLeaderboard).catch(() => {});
  }, [connection]);

  async function handleCreate() {
    try {
      const room = await connection.invoke("CreateRoom", maxPlayers);
      onJoinRoom(room.roomId, room.maxPlayers, 1);
    } catch {
      setError("Insufficient balance or failed to create room");
    }
  }

  async function handleJoin() {
    try {
      const result = await connection.invoke("JoinRoom", roomId);
      onJoinRoom(roomId, result.maxPlayers, result.playerCount);
    } catch {
      setError("Insufficient balance or failed to join room");
    }
  }

  async function handleClaimBonus() {
    try {
      const newBalance = await connection.invoke("ClaimBonus");
      setBalance(newBalance);
      setError("");
    } catch {
      setError("Failed to claim bonus");
    }
  }

  return (
    <div className="page-center" style={{ maxWidth: 460 }}>
      <h2>BlackJack Lobby</h2>
      {balance !== null && (
        <p className="text-gold mb-16">{nickname}'s balance: {balance}</p>
      )}
      {balance !== null && balance < 50 && (
        <div className="mb-16">
          <button onClick={handleClaimBonus} style={{ background: '#f59e0b' }}>
            Claim Bonus (10~20)
          </button>
        </div>
      )}

      <div className="section">
        <h3>Create Room</h3>
        <div className="inline-group" style={{ flexWrap: "wrap", gap: 10 }}>
          <label className="text-muted" style={{ whiteSpace: 'nowrap' }}>Max Players:</label>
          <WheelPicker value={maxPlayers} min={1} max={7} onChange={setMaxPlayers} itemHeight={26} width={56} />
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

      {error && <p className="error-msg">{error}</p>}

      {leaderboard.length > 0 && (
        <div className="section">
          <h3>Leaderboard</h3>
          <Leaderboard entries={leaderboard} nickname={nickname} valueLabel="Balance" />
        </div>
      )}
      <hr />
      <button onClick={onBack} style={{ background: '#94a3b8' }}>Back</button>
    </div>
  );
}
