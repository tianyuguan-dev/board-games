import { useState, useEffect } from "react";
import { updateNickname, changePassword, getBalances } from "../services/api";
import "./Profile.css";

const GAME_LABELS = {
  BlackJack: "BlackJack",
};

export default function Profile({ token, nickname, onNicknameChange, onBack }) {
  const [editNickname, setEditNickname] = useState(nickname);
  const [oldPassword, setOldPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [balances, setBalances] = useState(null);
  const [message, setMessage] = useState("");
  const [messageType, setMessageType] = useState("");

  useEffect(() => {
    getBalances(token).then(setBalances).catch(() => {});
  }, [token]);

  function showMessage(msg, type) {
    setMessage(msg);
    setMessageType(type);
  }

  async function handleSaveNickname() {
    try {
      const result = await updateNickname(token, editNickname);
      onNicknameChange(result.nickname);
      showMessage("Nickname updated", "success");
    } catch {
      showMessage("Failed to update nickname", "error");
    }
  }

  async function handleChangePassword() {
    if (newPassword !== confirmPassword) {
      showMessage("Passwords do not match", "error");
      return;
    }
    if (!newPassword) {
      showMessage("New password cannot be empty", "error");
      return;
    }
    try {
      await changePassword(token, oldPassword, newPassword);
      setOldPassword("");
      setNewPassword("");
      setConfirmPassword("");
      showMessage("Password changed successfully", "success");
    } catch (e) {
      showMessage(e.message || "Failed to change password", "error");
    }
  }

  return (
    <div className="profile-container">
      <div className="profile-header">
        <h2>Profile</h2>
        <button onClick={onBack} style={{ background: '#94a3b8' }}>Back</button>
      </div>

      <div className="profile-section">
        <h3>Nickname</h3>
        <div className="inline-group">
          <input
            type="text"
            value={editNickname}
            onChange={(e) => setEditNickname(e.target.value)}
          />
          <button
            onClick={handleSaveNickname}
            disabled={editNickname === nickname || !editNickname}
          >
            Save
          </button>
        </div>
      </div>

      <div className="profile-section">
        <h3>Game Balances</h3>
        {balances ? (
          <table className="balance-table">
            <thead>
              <tr><th>Game</th><th>Balance</th></tr>
            </thead>
            <tbody>
              {Object.entries(balances).map(([game, bal]) => (
                <tr key={game}>
                  <td>{GAME_LABELS[game] || game}</td>
                  <td>{bal}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <p className="text-muted">Loading...</p>
        )}
      </div>

      <div className="profile-section">
        <h3>Change Password</h3>
        <div className="form-group">
          <label>Current Password</label>
          <input type="password" value={oldPassword} onChange={(e) => setOldPassword(e.target.value)} />
        </div>
        <div className="form-group">
          <label>New Password</label>
          <input type="password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
        </div>
        <div className="form-group">
          <label>Confirm New Password</label>
          <input type="password" value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} />
        </div>
        <button onClick={handleChangePassword} disabled={!oldPassword || !newPassword}>
          Change Password
        </button>
      </div>

      {message && (
        <p className={messageType === "success" ? "success-msg" : "error-msg"}>{message}</p>
      )}
    </div>
  );
}
