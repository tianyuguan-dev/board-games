import { useState } from "react";
import { login, register } from "../services/api";

export default function Login({ onLogin }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [isRegister, setIsRegister] = useState(false);
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
    if (password !== confirmPassword) {
      setError("Passwords do not match");
      return;
    }
    try {
      await register(username, password);
      const data = await login(username, password);
      onLogin(data.token, data.nickname);
    } catch (e) {
      setError(e.message || "Register failed");
    }
  }

  return (
    <div className="page-center">
      <h2>Guan Yu Board Games</h2>
      <div className="form-group">
        <label>Username</label>
        <input
          type="text"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
        />
      </div>
      <div className="form-group">
        <label>Password</label>
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
      </div>
      {isRegister && (
        <div className="form-group">
          <label>Confirm Password</label>
          <input
            type="password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
          />
        </div>
      )}
      <div className="btn-row">
        {isRegister ? (
          <>
            <button onClick={handleRegister}>Register</button>
            <button onClick={() => { setIsRegister(false); setError(""); }} style={{ background: '#94a3b8' }}>Back to Login</button>
          </>
        ) : (
          <>
            <button onClick={handleLogin}>Login</button>
            <button onClick={() => { setIsRegister(true); setError(""); }} style={{ background: '#94a3b8' }}>Register</button>
          </>
        )}
      </div>
      {error && <p className={error.includes("Registered") ? "success-msg" : "error-msg"}>{error}</p>}
    </div>
  );
}
