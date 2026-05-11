import { useState } from "react";
import { updateNickname } from "../services/api";

export default function Lobby({ connection, token, nickname, onNicknameChange, onJoinRoom, onLogout }) {
  const [roomId, setRoomId] = useState("");
  const [maxPlayers, setMaxPlayers] = useState(4);
  const [editNickname, setEditNickname] = useState(nickname);
  const [error, setError] = useState("");

  async function handleCreate() {
    try {
      const room = await connection.invoke("CreateRoom", maxPlayers);
      onJoinRoom(room.roomId, room.maxPlayers, 1);
    } catch {
      setError("Failed to create room");
    }
  }

  async function handleJoin() {
    try {
      const result = await connection.invoke("JoinRoom", roomId);
      onJoinRoom(roomId, result.maxPlayers, result.playerCount);
    } catch {
      setError("Failed to join room");
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
    <div>
      <h2>Lobby</h2>

      <div>
        <h3>Nickname</h3>
        <input
          value={editNickname}
          onChange={(e) => setEditNickname(e.target.value)}
        />
        {editNickname !== nickname && (
          <button onClick={handleSaveNickname}>Save</button>
        )}
        {editNickname === nickname && <span> ✓</span>}
      </div>

      <div>
        <h3>Create Room</h3>
        <label>Max Players: </label>
        <input
          type="number"
          min="1"
          max="7"
          value={maxPlayers}
          onChange={(e) => setMaxPlayers(Number(e.target.value))}
        />
        <br />
        <button onClick={handleCreate}>Create</button>
      </div>

      <div>
        <h3>Join Room</h3>
        <input
          placeholder="Room ID"
          value={roomId}
          onChange={(e) => setRoomId(e.target.value)}
        />
        <br />
        <button onClick={handleJoin}>Join</button>
      </div>

      {error && <p style={{ color: "red" }}>{error}</p>}
      <hr />
      <button onClick={onLogout}>Logout</button>
    </div>
  );
}
