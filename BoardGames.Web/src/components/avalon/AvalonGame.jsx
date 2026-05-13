import { useState, useEffect } from "react";
import "./AvalonGame.css";

const TOGGLE_EVIL = ["Mordred", "Oberon"];

const ROLE_LABELS = {
  Merlin: { name: "Merlin", emoji: "\uD83E\uDDD9", desc: "Knows evil (except Mordred)" },
  Percival: { name: "Percival", emoji: "\uD83D\uDC82", desc: "Sees Merlin & Morgana" },
  LoyalServant: { name: "Loyal Servant", emoji: "\uD83D\uDEE1\uFE0F", desc: "No special power" },
  Assassin: { name: "Assassin", emoji: "\uD83D\uDDE1\uFE0F", desc: "Can kill Merlin at end" },
  Morgana: { name: "Morgana", emoji: "\uD83D\uDC8B", desc: "Appears as Merlin to Percival" },
  Mordred: { name: "Mordred", emoji: "\uD83D\uDE08", desc: "Hidden from Merlin" },
  Oberon: { name: "Oberon", emoji: "\uD83E\uDD21", desc: "Unknown to other evil" },
  MinionOfMordred: { name: "Minion", emoji: "\uD83E\uDD77", desc: "Basic evil" },
};

function groupRoles(roles) {
  const counts = {};
  const order = [];
  for (const r of roles) {
    if (!counts[r]) { counts[r] = 0; order.push(r); }
    counts[r]++;
  }
  return order.map((r) => ({ role: r, count: counts[r] }));
}

export default function AvalonGame({ connection, nickname, roomId, maxPlayers, playerCount, roomPlayers, isHost, roleConfig, maxRejects, needsRejoin, gameInProgress, onLeave }) {
  const [gameState, setGameState] = useState(null);
  const [ready, setReady] = useState(false);
  const [myIndex, setMyIndex] = useState(-1);
  const [selectedTeam, setSelectedTeam] = useState([]);
  const [nightConfirmed, setNightConfirmed] = useState(false);
  const [hasVoted, setHasVoted] = useState(false);
  const [hasPlayedCard, setHasPlayedCard] = useState(false);
  const [balance, setBalance] = useState(null);

  useEffect(() => {
    connection.on("YourSeat", (index) => setMyIndex(index));
    connection.on("GameState", (state) => {
      setGameState(state);
      setReady(false);
      setNightConfirmed(false);
      setHasVoted(false);
      setHasPlayedCard(false);
      setSelectedTeam([]);
    });
    connection.on("VoteProgress", () => {});
    connection.on("MissionProgress", () => {});
    connection.on("NightRevealProgress", () => {});
    connection.on("BalanceUpdate", (bal) => setBalance(bal));
    connection.invoke("GetBalance").then(setBalance).catch(() => {});
    connection.on("GameAborted", (reason) => {
      alert(reason);
      setGameState(null);
    });

    // Try rejoin only after page refresh (not after normal Create/Join)
    if (needsRejoin && needsRejoin.current) {
      needsRejoin.current = false;
      connection.invoke("Rejoin", roomId).catch(() => {
        connection.invoke("JoinRoom", roomId).catch(() => {
          sessionStorage.removeItem("roomId");
          onLeave();
        });
      });
    } else if (gameInProgress) {
      // Joined a game-in-progress from lobby; GameState was sent before handlers registered.
      // Request it again now that handlers are ready.
      connection.invoke("GetGameState", roomId).catch(() => {});
    }

    return () => {
      connection.off("YourSeat");
      connection.off("GameState");
      connection.off("VoteProgress");
      connection.off("MissionProgress");
      connection.off("NightRevealProgress");
      connection.off("BalanceUpdate");
      connection.off("GameAborted");
    };
  }, [connection]);

  async function handleReady() {
    setReady(true);
    await connection.invoke("Ready", roomId);
  }
  async function handleUnready() {
    await connection.invoke("Unready", roomId);
    setReady(false);
  }
  async function handleStart() {
    try {
      await connection.invoke("StartGame", roomId);
    } catch (e) {
      alert(e.message);
    }
  }
  async function handleAdjustRole(role, delta) {
    try {
      await connection.invoke("AdjustRole", roomId, role, delta);
    } catch (e) {
      // silently ignore (e.g. can't reduce below 0 or exceed good minimum)
    }
  }
  async function handleKick(seatIndex) {
    await connection.invoke("KickPlayer", roomId, seatIndex);
  }
  async function handleMove(seatIndex, direction) {
    await connection.invoke("MovePlayer", roomId, seatIndex, direction);
  }
  async function handleLeave() {
    if (gameState && gameState.phase !== "GameOver") {
      if (!window.confirm("Leaving during a game will abort the game and you will lose 5 points. Are you sure?")) {
        return;
      }
    }
    await connection.invoke("LeaveRoom");
    onLeave();
  }
  async function handleConfirmNight() {
    setNightConfirmed(true);
    await connection.invoke("ConfirmNightReveal", roomId);
  }
  async function handleProposeTeam() {
    await connection.invoke("ProposeTeam", roomId, selectedTeam);
  }
  async function handleVote(approve) {
    if (!approve && gameState && gameState.consecutiveRejects >= (gameState.maxConsecutiveRejects || 5) - 1) {
      if (!window.confirm("This is the last chance! If this proposal is rejected, the good side loses immediately. Are you sure?")) {
        return;
      }
    }
    await connection.invoke("CastVote", roomId, approve);
    setHasVoted(true);
  }
  async function handleMissionCard(success) {
    await connection.invoke("PlayMissionCard", roomId, success);
    setHasPlayedCard(true);
  }
  async function handleAssassinate(targetIndex) {
    await connection.invoke("Assassinate", roomId, targetIndex);
  }

  function toggleTeamMember(index) {
    setSelectedTeam((prev) =>
      prev.includes(index) ? prev.filter((i) => i !== index) : [...prev, index]
    );
  }

  function renderPlayerList() {
    if (roomPlayers.length === 0) return null;
    return (
      <ul className="av-player-list">
        {roomPlayers.map((p, i) => (
          <li key={i}>
            <span className="seat-number">{i + 1}.</span>
            {p.nickname} {p.isHost ? "\uD83D\uDC51" : p.isReady ? "\u2713" : "\u2014"}
            {isHost && (
              <span className="player-actions">
                <button className="move-btn" disabled={i === 0} onClick={() => handleMove(p.seatIndex, -1)}>&uarr;</button>
                <button className="move-btn" disabled={i === roomPlayers.length - 1} onClick={() => handleMove(p.seatIndex, 1)}>&darr;</button>
                {!p.isHost && <button className="kick-btn" onClick={() => handleKick(p.seatIndex)}>Kick</button>}
              </span>
            )}
          </li>
        ))}
      </ul>
    );
  }

  function renderMissionTrack() {
    return (
      <div className="mission-track">
        {gameState.missionResults.map((r, i) => (
          <div
            key={i}
            className={`mission-dot ${r === "Success" ? "success" : r === "Fail" ? "fail" : ""} ${i === gameState.currentMissionIndex ? "current" : ""}`}
          >
            {i + 1}
          </div>
        ))}
      </div>
    );
  }

  function renderRejectTrack() {
    return (
      <div className="reject-track">
        Vote track: {[...Array(gameState.maxConsecutiveRejects || 5)].map((_, i) => (
          <span key={i} className={`reject-dot ${i < gameState.consecutiveRejects ? "active" : ""}`} />
        ))}
      </div>
    );
  }

  function renderHistory() {
    if (!gameState.history || gameState.history.every((p) => p.length === 0)) return null;
    return (
      <div className="history-panel">
        <h3>History</h3>
        {gameState.history.map((proposals, mi) => (
          <div key={mi} className="history-mission">
            <h4>Mission {mi + 1} {gameState.missionResults[mi] !== "Pending" && `(${gameState.missionResults[mi]})`}</h4>
            {proposals.length === 0 && <p className="text-muted">No proposals yet</p>}
            {proposals.map((p, pi) => (
              <div key={pi} className="history-proposal">
                <div className="proposal-header">
                  Proposal #{pi + 1} by <strong>{gameState.playerNames[p.leaderIndex]}</strong>
                  {" "}{p.approved ? <span className="badge-approve">Approved</span> : <span className="badge-reject">Rejected</span>}
                </div>
                <div className="proposal-team">
                  Team: {p.team.map((t) => gameState.playerNames[t]).join(", ")}
                </div>
                {p.votes && (
                  <div className="proposal-votes">
                    {Object.entries(p.votes).map(([idx, vote]) => (
                      <span key={idx} className={`vote-chip ${vote ? "approve" : "reject"}`}>
                        {gameState.playerNames[idx]} {vote ? "\u2713" : "\u2717"}
                      </span>
                    ))}
                  </div>
                )}
                {p.missionResult && p.missionResult !== "Pending" && (
                  <div className="proposal-mission-result">
                    Result: {p.successCount} success, {p.failCount} fail
                    {" "}<span className={p.missionResult === "Success" ? "badge-approve" : "badge-reject"}>{p.missionResult}</span>
                  </div>
                )}
              </div>
            ))}
          </div>
        ))}
      </div>
    );
  }

  // Pre-game lobby
  if (!gameState) {
    const canStart = roomPlayers.length >= 5 &&
      (roomPlayers.length <= 1 || roomPlayers.filter((p) => !p.isHost).every((p) => p.isReady));
    return (
      <div className="page-center" style={{ maxWidth: 500 }}>
        <h2>Room {roomId}</h2>
        <p className="text-muted">Players: {playerCount} / {maxPlayers} (need 5-10){balance !== null && ` | ${nickname || "You"}'s net wins: ${balance}`}</p>
        {renderPlayerList()}

        {roleConfig.length > 0 && (() => {
          const goodRoles = roleConfig.filter((r) => ["Merlin","Percival","LoyalServant"].includes(r));
          const evilRoles = roleConfig.filter((r) => !["Merlin","Percival","LoyalServant"].includes(r));
          const counts = {};
          for (const r of evilRoles) counts[r] = (counts[r] || 0) + 1;
          const canAddEvil = goodRoles.length - evilRoles.length > 2 && goodRoles.length > 2;
          return (
            <div className="section">
              <h3>Role Config ({roleConfig.length} players)</h3>
              <div className="role-config">
                <div className="role-config-side">
                  <span className="side-label good-label">Good ({goodRoles.length})</span>
                  {groupRoles(goodRoles).map(({ role, count }, i) => (
                    <span key={i} className="role-chip good">{ROLE_LABELS[role]?.emoji} {ROLE_LABELS[role]?.name || role}{count > 1 ? ` x${count}` : ""}</span>
                  ))}
                </div>
                <div className="role-config-side">
                  <span className="side-label evil-label">Evil ({evilRoles.length})</span>
                  {groupRoles(evilRoles).map(({ role, count }, i) => (
                    <span key={i} className="role-chip evil">{ROLE_LABELS[role]?.emoji} {ROLE_LABELS[role]?.name || role}{count > 1 ? ` x${count}` : ""}</span>
                  ))}
                </div>
              </div>
              {isHost && (
                <div className="role-adjust-section">
                  <div className="role-toggles">
                    {TOGGLE_EVIL.map((role) => (
                      <button
                        key={role}
                        className={`role-toggle ${counts[role] ? "active" : ""}`}
                        disabled={!counts[role] && !canAddEvil}
                        onClick={() => handleAdjustRole(role, counts[role] ? -1 : 1)}
                      >
                        {ROLE_LABELS[role]?.emoji} {ROLE_LABELS[role]?.name}
                      </button>
                    ))}
                  </div>
                  <div className="role-adjust-row">
                    <button className="role-adjust-btn" disabled={(counts["MinionOfMordred"] || 0) <= 0} onClick={() => handleAdjustRole("MinionOfMordred", -1)}>-</button>
                    <span className="role-adjust-label">{ROLE_LABELS.MinionOfMordred.emoji} {ROLE_LABELS.MinionOfMordred.name} {counts["MinionOfMordred"] || 0}</span>
                    <button className="role-adjust-btn" disabled={!canAddEvil} onClick={() => handleAdjustRole("MinionOfMordred", 1)}>+</button>
                  </div>
                </div>
              )}

              {isHost && (
                <div className="role-adjust-section" style={{ marginTop: 12 }}>
                  <div className="role-adjust-row">
                    <button className="role-adjust-btn" disabled={maxRejects <= 1} onClick={() => connection.invoke("SetMaxRejects", roomId, maxRejects - 1).catch(() => {})}>-</button>
                    <span className="role-adjust-label">Max Rejects: {maxRejects}</span>
                    <button className="role-adjust-btn" disabled={maxRejects >= 10} onClick={() => connection.invoke("SetMaxRejects", roomId, maxRejects + 1).catch(() => {})}>+</button>
                  </div>
                </div>
              )}
              {!isHost && <p className="text-muted" style={{ fontSize: 13, marginTop: 8 }}>Max consecutive rejects: {maxRejects}</p>}
            </div>
          );
        })()}

        <div className="btn-row">
          {isHost ? (
            <button className="btn btn-success" onClick={handleStart} disabled={!canStart}>
              Start Game
            </button>
          ) : ready ? (
            <button className="btn btn-warning" onClick={handleUnready}>Cancel Ready</button>
          ) : (
            <button className="btn btn-success" onClick={handleReady}>Ready</button>
          )}
          <button className="btn btn-outline" onClick={handleLeave}>Leave</button>
        </div>
      </div>
    );
  }

  const gs = gameState;
  const myRole = ROLE_LABELS[gs.myRole] || { name: gs.myRole, emoji: "", desc: "" };
  const isEvil = gs.myTeam === "Evil";
  const isLeader = myIndex === gs.currentLeaderIndex;

  // Night Reveal
  if (gs.phase === "NightReveal") {
    return (
      <div className="av-container">
        <div className="av-night">
          <h2>Night Phase</h2>
          <div className={`role-card ${isEvil ? "evil" : "good"}`}>
            <span className="role-emoji">{myRole.emoji}</span>
            <h3>{myRole.name}</h3>
            <p className="role-team">{gs.myTeam}</p>
            <p className="role-desc">{myRole.desc}</p>
          </div>
          {gs.visiblePlayers && gs.visiblePlayers.length > 0 && (
            <div className="night-info">
              <p className="night-hint">{gs.visibleHint}:</p>
              <div className="night-players">
                {gs.visiblePlayers.map((i) => (
                  <span key={i} className="night-player">
                    {gs.playerNames[i]}
                    {gs.visiblePlayerRoles && gs.visiblePlayerRoles[i] && (
                      <span style={{ fontWeight: 400, fontSize: 12 }}> ({ROLE_LABELS[gs.visiblePlayerRoles[i]]?.name || gs.visiblePlayerRoles[i]})</span>
                    )}
                  </span>
                ))}
              </div>
            </div>
          )}
          <div style={{ marginTop: 20 }}>
            {!nightConfirmed ? (
              <button className="btn btn-success" onClick={handleConfirmNight}>I've seen my role</button>
            ) : (
              <p className="text-muted">Waiting for others...</p>
            )}
          </div>
        </div>
      </div>
    );
  }

  // Game phases
  return (
    <div className="av-container">
      <div className="av-header">
        <span className="room-info">Room {roomId}{balance !== null && ` | ${nickname || "You"}'s net wins: ${balance}`}</span>
        <span className={`role-badge ${isEvil ? "evil" : "good"}`}>
          {myRole.emoji} {myRole.name}
        </span>
        <button className="btn-small" style={{ color: "#dc2626" }} onClick={handleLeave}>Leave</button>
      </div>

      {gs.visiblePlayers && gs.visiblePlayers.length > 0 && (
        <div className="night-info-bar">
          <span className="night-info-hint">{gs.visibleHint}:</span>
          {gs.visiblePlayers.map((i) => (
            <span key={i} className="night-info-player">
              {gs.playerNames[i]}
              {gs.visiblePlayerRoles && gs.visiblePlayerRoles[i] && (
                <span className="night-info-role"> ({ROLE_LABELS[gs.visiblePlayerRoles[i]]?.name || gs.visiblePlayerRoles[i]})</span>
              )}
            </span>
          ))}
        </div>
      )}

      {renderMissionTrack()}
      {renderRejectTrack()}

      {/* Player circle */}
      <div className="av-players">
        {gs.playerNames.map((name, i) => {
          const onTeam = gs.proposedTeam.includes(i);
          const isCurrLeader = i === gs.currentLeaderIndex;
          const isMe = i === myIndex;
          const selectable = gs.phase === "TeamProposal" && isLeader;
          const selected = selectedTeam.includes(i);
          const assassinating = gs.phase === "Assassination" && myIndex === gs.assassinIndex;
          return (
            <div
              key={i}
              className={`av-player ${isMe ? "me" : ""} ${onTeam ? "on-team" : ""} ${isCurrLeader ? "leader" : ""} ${selected ? "selected" : ""} ${selectable || assassinating ? "clickable" : ""}`}
              onClick={() => {
                if (selectable) toggleTeamMember(i);
                if (assassinating) handleAssassinate(i);
              }}
            >
              <span className="player-name">{name} {isCurrLeader ? "\uD83D\uDC51" : ""}</span>
              {isMe && <span className="me-tag">(You)</span>}
            </div>
          );
        })}
      </div>

      {/* Phase-specific actions */}
      <div className="av-actions">
        {gs.phase === "TeamProposal" && isLeader && (
          <div className="action-panel">
            <p>Select {gs.requiredTeamSize} players for the mission ({selectedTeam.length}/{gs.requiredTeamSize})</p>
            <button
              className="btn btn-success"
              onClick={handleProposeTeam}
              disabled={selectedTeam.length !== gs.requiredTeamSize}
            >
              Confirm Team
            </button>
          </div>
        )}
        {gs.phase === "TeamProposal" && !isLeader && (
          <p className="waiting">Waiting for {gs.playerNames[gs.currentLeaderIndex]} to propose a team...</p>
        )}

        {gs.phase === "TeamVote" && (
          <div className="action-panel">
            <p>Team: {gs.proposedTeam.map((i) => gs.playerNames[i]).join(", ")}</p>
            {hasVoted ? (
              <p className="waiting">Voted! Waiting for others...</p>
            ) : (
              <div className="btn-row">
                <button className="btn btn-success" onClick={() => handleVote(true)}>Approve</button>
                <button className="btn btn-danger" onClick={() => handleVote(false)}>Reject</button>
              </div>
            )}
          </div>
        )}

        {gs.phase === "Mission" && gs.proposedTeam?.includes(myIndex) && (
          <div className="action-panel">
            {hasPlayedCard ? (
              <p className="waiting">Card played! Waiting for others...</p>
            ) : (
              <>
                <p>You're on the mission! Play your card:</p>
                <div className="btn-row">
                  <button className="btn btn-success" onClick={() => handleMissionCard(true)}>Success</button>
                  {isEvil && (
                    <button className="btn btn-danger" onClick={() => handleMissionCard(false)}>Fail</button>
                  )}
                </div>
              </>
            )}
          </div>
        )}
        {gs.phase === "Mission" && !gs.proposedTeam?.includes(myIndex) && (
          <p className="waiting">Mission in progress...</p>
        )}

        {gs.phase === "Assassination" && myIndex === gs.assassinIndex && (
          <div className="action-panel">
            {gs.bonusAssassination
              ? <p>Evil won 3 missions! Find Merlin for double points! Click on your target.</p>
              : <p>Good side won 3 missions. Click on who you think is Merlin!</p>
            }
          </div>
        )}
        {gs.phase === "Assassination" && myIndex !== gs.assassinIndex && (
          <p className="waiting">
            {gs.bonusAssassination
              ? "Evil won 3 missions. Assassin is attempting to find Merlin for double points..."
              : "Assassin is choosing a target..."}
          </p>
        )}

        {gs.phase === "GameOver" && (
          <div className="game-over">
            <h2 className={gs.winner === "Good" ? "good-win" : "evil-win"}>
              {gs.winner === "Good" ? "Good Wins!" : "Evil Wins!"}
            </h2>
            <p>{gs.winReason}</p>
            {gs.assassinTarget != null && (
              <p>Assassin targeted: {gs.playerNames[gs.assassinTarget]}</p>
            )}
            <div className="roles-reveal">
              {gs.allRoles.map((role, i) => (
                <div key={i} className={`role-reveal ${AvalonTeamForRole(role) === "Evil" ? "evil" : "good"}`}>
                  <strong>{gs.playerNames[i]}</strong>: {ROLE_LABELS[role]?.emoji} {ROLE_LABELS[role]?.name || role}
                </div>
              ))}
            </div>
            <div className="btn-row" style={{ marginTop: 16 }}>
              {isHost ? (
                <button className="btn btn-success" onClick={handleStart}
                  disabled={roomPlayers.filter((p) => !p.isHost).some((p) => !p.isReady) && roomPlayers.length > 1}>
                  Play Again
                </button>
              ) : ready ? (
                <button className="btn btn-warning" onClick={handleUnready}>Cancel Ready</button>
              ) : (
                <button className="btn btn-success" onClick={handleReady}>Ready</button>
              )}
              <button className="btn btn-outline" onClick={handleLeave}>Leave</button>
            </div>
          </div>
        )}
      </div>

      {renderHistory()}
    </div>
  );
}

function AvalonTeamForRole(role) {
  return ["Merlin", "Percival", "LoyalServant"].includes(role) ? "Good" : "Evil";
}
