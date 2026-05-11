import { useState } from "react";

export default function Lobby({ connection, onJoinRoom }) {
  const [roomId, setRoomId] = useState("");
  const [maxPlayers, setMaxPlayers] = useState(4);
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

  return (
    <div>
      <h2>Lobby</h2>

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
    </div>
  );
}
