import { useState, useEffect, useCallback } from "react";

const BASE_URL = import.meta.env.VITE_BACKEND_URL
  ? `${import.meta.env.VITE_BACKEND_URL}/api/admin`
  : (import.meta.env.PROD ? '/api/admin' : `http://${window.location.hostname}:5087/api/admin`);

export default function Admin() {
  const [password, setPassword] = useState(sessionStorage.getItem("adminToken") || "");
  const [authed, setAuthed] = useState(!!sessionStorage.getItem("adminToken"));
  const [users, setUsers] = useState([]);
  const [search, setSearch] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [selectedUser, setSelectedUser] = useState(null);
  const [editNickname, setEditNickname] = useState("");
  const [editBalances, setEditBalances] = useState({});
  const [newPassword, setNewPassword] = useState("");

  const headers = { "Content-Type": "application/json", "X-Admin-Token": password };

  function showSuccess(msg) {
    setSuccess(msg);
    setTimeout(() => setSuccess(""), 3000);
  }

  const fetchUsers = useCallback(async (query) => {
    setError("");
    try {
      const url = query ? `${BASE_URL}/users?search=${encodeURIComponent(query)}` : `${BASE_URL}/users`;
      const res = await fetch(url, { headers: { "X-Admin-Token": password } });
      if (!res.ok) { setAuthed(false); sessionStorage.removeItem("adminToken"); setError("Unauthorized"); return; }
      setUsers(await res.json());
    } catch { setError("Failed to fetch users"); }
  }, [password]);

  useEffect(() => {
    if (authed) fetchUsers(search);
  }, [authed]);

  function handleLogin() {
    sessionStorage.setItem("adminToken", password);
    setAuthed(true);
  }

  async function openUserDetail(userId) {
    setError(""); setSuccess("");
    try {
      const res = await fetch(`${BASE_URL}/users/${userId}`, { headers });
      if (!res.ok) { setError("Failed to load user"); return; }
      const data = await res.json();
      setSelectedUser(data);
      setEditNickname(data.nickname);
      const bals = {};
      for (const b of data.balances) bals[b.gameType] = b.balance;
      setEditBalances(bals);
    } catch { setError("Failed to load user"); }
  }

  async function saveNickname() {
    setError(""); setSuccess("");
    try {
      const res = await fetch(`${BASE_URL}/users/${selectedUser.id}/nickname`, {
        method: "PUT", headers,
        body: JSON.stringify({ nickname: editNickname }),
      });
      if (!res.ok) { setError("Failed to update nickname"); return; }
      const data = await res.json();
      showSuccess(data.message);
      setSelectedUser({ ...selectedUser, nickname: editNickname });
      setUsers(users.map(u => u.id === selectedUser.id ? { ...u, nickname: editNickname } : u));
    } catch { setError("Failed to update nickname"); }
  }

  async function saveBalance(gameType) {
    setError(""); setSuccess("");
    try {
      const res = await fetch(`${BASE_URL}/users/${selectedUser.id}/balance`, {
        method: "PUT", headers,
        body: JSON.stringify({ gameType, balance: Number(editBalances[gameType]) }),
      });
      if (!res.ok) { setError("Failed to update balance"); return; }
      const data = await res.json();
      showSuccess(data.message);
    } catch { setError("Failed to update balance"); }
  }

  async function handleResetPassword() {
    if (!newPassword.trim()) { setError("Password cannot be empty"); return; }
    setError(""); setSuccess("");
    try {
      const res = await fetch(`${BASE_URL}/reset-password`, {
        method: "POST", headers,
        body: JSON.stringify({ userId: selectedUser.id, newPassword }),
      });
      if (!res.ok) { setError("Reset failed"); return; }
      const data = await res.json();
      showSuccess(data.message);
      setNewPassword("");
    } catch { setError("Reset failed"); }
  }

  if (!authed) {
    return (
      <div className="page-center">
        <h2>Admin</h2>
        <div className="form-group">
          <label>Admin Password</label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleLogin()} />
        </div>
        <button onClick={handleLogin}>Login</button>
        {error && <p className="error-msg">{error}</p>}
      </div>
    );
  }

  if (selectedUser) {
    return (
      <div className="page-center" style={{ maxWidth: 500 }}>
        <h2>User Detail</h2>
        {error && <p className="error-msg">{error}</p>}
        {success && <p className="success-msg" style={{ background: "#dcfce7", padding: "8px 12px", borderRadius: 6 }}>{success}</p>}

        <div className="section">
          <p className="text-muted">ID: {selectedUser.id} &nbsp;|&nbsp; Username: {selectedUser.username}</p>
          <p className="text-muted">
            Created: {new Date(selectedUser.createdAt).toLocaleDateString()}
            &nbsp;|&nbsp; Last Active: {selectedUser.lastActiveAt ? new Date(selectedUser.lastActiveAt).toLocaleDateString() : "-"}
          </p>
        </div>

        <div className="section">
          <h3>Nickname</h3>
          <div className="inline-group">
            <input type="text" value={editNickname} onChange={(e) => setEditNickname(e.target.value)} style={{ flex: 1 }} />
            <button onClick={saveNickname}>Save</button>
          </div>
        </div>

        <div className="section">
          <h3>Balances</h3>
          {["BlackJack", "Avalon"].map(gt => (
            <div key={gt} className="inline-group" style={{ marginBottom: 8 }}>
              <span style={{ width: 80 }}>{gt}</span>
              <input type="number" step="0.5" value={editBalances[gt] ?? 0}
                onChange={(e) => setEditBalances({ ...editBalances, [gt]: e.target.value })}
                style={{ width: 100 }} />
              <button onClick={() => saveBalance(gt)} style={{ padding: "4px 12px", fontSize: 12 }}>Save</button>
            </div>
          ))}
        </div>

        <div className="section">
          <h3>Reset Password</h3>
          <div className="inline-group">
            <input type="text" placeholder="New password" value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)} style={{ flex: 1 }} />
            <button onClick={handleResetPassword}>Reset</button>
          </div>
        </div>

        <hr />
        <button onClick={() => { setSelectedUser(null); setSuccess(""); setError(""); }} style={{ background: "#94a3b8" }}>Back</button>
      </div>
    );
  }

  return (
    <div className="page-center" style={{ maxWidth: 600 }}>
      <h2>Admin Panel</h2>

      <div className="inline-group" style={{ marginBottom: 16 }}>
        <input type="text" placeholder="Search username / nickname" value={search}
          onChange={(e) => setSearch(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && fetchUsers(search)}
          style={{ flex: 1 }} />
        <button onClick={() => fetchUsers(search)}>Search</button>
      </div>

      {error && <p className="error-msg">{error}</p>}
      {success && <p className="success-msg" style={{ background: "#dcfce7", padding: "8px 12px", borderRadius: 6 }}>{success}</p>}

      <table className="leaderboard-table">
        <thead>
          <tr>
            <th>ID</th>
            <th>Username</th>
            <th>Nickname</th>
            <th>Last Active</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {users.map((u) => (
            <tr key={u.id} onClick={() => openUserDetail(u.id)} style={{ cursor: "pointer" }}>
              <td>{u.id}</td>
              <td>{u.username}</td>
              <td>{u.nickname}</td>
              <td>{u.lastActiveAt ? new Date(u.lastActiveAt).toLocaleDateString() : "-"}</td>
              <td>
                <button onClick={(e) => { e.stopPropagation(); openUserDetail(u.id); }}
                  style={{ padding: "4px 10px", fontSize: 12 }}>
                  Detail
                </button>
              </td>
            </tr>
          ))}
          {users.length === 0 && (
            <tr><td colSpan={5} style={{ textAlign: "center", color: "#94a3b8" }}>No users found</td></tr>
          )}
        </tbody>
      </table>

      <hr />
      <button onClick={() => { setAuthed(false); sessionStorage.removeItem("adminToken"); }}
        style={{ background: "#94a3b8" }}>Logout</button>
    </div>
  );
}
