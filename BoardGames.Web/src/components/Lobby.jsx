import { useState, useEffect } from "react";
import { updateNickname } from "../services/api";

export default function Lobby({ connection, token, nickname, onNicknameChange, onJoinRoom, onLogout }) {
  const [roomId, setRoomId] = useState("");
  const [maxPlayers, setMaxPlayers] = useState(4);
  const [editNickname, setEditNickname] = useState(nickname);
  const [error, setError] = useState("");
  const [balance, setBalance] = useState(null);

  useEffect(() => {
    connection.invoke("GetBalance").then(setBalance).catch(() => {});
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

  async function handleSaveNickname() {
    try {
      const result = await updateNickname(token, editNickname);
      onNicknameChange(result.nickname);
      setError("");
    } catch {
      setError("Failed to update nickname");
    }
  }

  return (
    <div className="page-center">
      <h2>Lobby</h2>
      {balance !== null && (
        <p className="text-gold mb-16">Balance: {balance}</p>
      )}
      {balance !== null && balance < 50 && (
        <div className="mb-16">
          <button onClick={handleClaimBonus} style={{ background: '#f59e0b' }}>
            Claim Bonus (10~20)
          </button>
        </div>
      )}

      <div className="section">
        <h3>Nickname</h3>
        <div className="inline-group">
          <input
            value={editNickname}
            onChange={(e) => setEditNickname(e.target.value)}
          />
          {editNickname !== nickname ? (
            <button onClick={handleSaveNickname} style={{ background: '#22c55e' }}>Save</button>
          ) : (
            <span style={{ color: '#4ade80' }}>&#10003;</span>
          )}
        </div>
      </div>

      <div className="section">
        <h3>Create Room</h3>
        <div className="inline-group">
          <label className="text-muted" style={{ whiteSpace: 'nowrap' }}>Max Players:</label>
          <input
            type="number"
            min="1"
            max="7"
            value={maxPlayers}
            onChange={(e) => setMaxPlayers(Number(e.target.value))}
            style={{ width: 60 }}
          />
          <button onClick={handleCreate}>Create</button>
        </div>
      </div>

      <div className="section">
        <h3>Join Room</h3>
        <div className="inline-group">
          <input
            placeholder="Room ID"
            value={roomId}
            onChange={(e) => setRoomId(e.target.value)}
            style={{ width: 120 }}
          />
          <button onClick={handleJoin}>Join</button>
        </div>
      </div>

      {error && <p className="error-msg">{error}</p>}
      <hr />
      <button onClick={onLogout} style={{ background: '#94a3b8' }}>Logout</button>
    </div>
  );
}
