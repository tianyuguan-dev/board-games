import { useState } from "react";
import { login, register, loginAsGuest } from "../services/api";

export default function Login({ onLogin }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [isRegister, setIsRegister] = useState(false);
  const [error, setError] = useState("");

  async function handleLogin() {
    try {
      const data = await login(username, password);
      onLogin(data.token, data.refreshToken, data.nickname);
    } catch {
      setError("Login failed");
    }
  }

  async function handleGuestLogin() {
    try {
      const data = await loginAsGuest();
      onLogin(data.token, data.refreshToken, data.nickname);
    } catch (e) {
      setError(e.message || "Guest login failed");
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
      onLogin(data.token, data.refreshToken, data.nickname);
    } catch (e) {
      setError(e.message || "Register failed");
    }
  }

  return (
    <div className="page-center">
      <img
        src="/icon.jpg"
        alt="Guan Yu Board Games"
        style={{ width: 96, height: 96, borderRadius: 16, display: "block", margin: "0 auto 12px", boxShadow: "0 4px 14px rgba(0,0,0,0.12)" }}
      />
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
      {!isRegister && (
        <>
          <hr style={{ margin: "16px 0", border: "none", borderTop: "1px solid #e2e8f0" }} />
          <button onClick={handleGuestLogin} style={{ background: "#6366f1", width: "100%" }}>
            Try as Guest (Solo Demo)
          </button>
          <p className="text-muted" style={{ fontSize: 12, textAlign: "center", marginTop: 8 }}>
            One-click access: scripted Avalon solo demo + BlackJack vs dealer. No signup needed.
          </p>
        </>
      )}
      {error && <p className={error.includes("Registered") ? "success-msg" : "error-msg"}>{error}</p>}
    </div>
  );
}
