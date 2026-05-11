import { useState } from "react";
import { login, register } from "../services/api";

export default function Login({ onLogin }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");

  async function handleLogin() {
    try {
      const data = await login(username, password);
      onLogin(data.token, data.nickname);
    } catch {
      setError("Login failed");
    }
  }

  async function handleRegister() {
    try {
      await register(username, password);
      setError("Registered! Now login.");
    } catch {
      setError("Register failed");
    }
  }

  return (
    <div>
      <h2>BlackJack Login</h2>
      <input
        placeholder="Username"
        value={username}
        onChange={(e) => setUsername(e.target.value)}
      />
      <br />
      <input
        placeholder="Password"
        type="password"
        value={password}
        onChange={(e) => setPassword(e.target.value)}
      />
      <br />
      <button onClick={handleLogin}>Login</button>
      <button onClick={handleRegister}>Register</button>
      {error && <p style={{ color: "red" }}>{error}</p>}
    </div>
  );
}
