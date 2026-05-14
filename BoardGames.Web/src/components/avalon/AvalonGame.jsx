import { useState, useEffect, useRef, useCallback } from "react";
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
      setMyIndex(state.myIndex);
      setReady(false);
      setNightConfirmed(false);
      setSelectedTeam([]);
      // Restore vote/mission state on reconnect
      const mi = state.myIndex;
      setHasVoted(!!(state.phase === "TeamVote" && state.playersWhoVoted?.includes(mi)));
      setHasPlayedCard(!!(state.phase === "Mission" && state.missionPlayersPlayed?.includes(mi)));
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
  const [dragFrom, setDragFrom] = useState(null);
  const [dropTarget, setDropTarget] = useState(null); // insert position index
  const listRef = useRef(null);

  function calcDropTarget(clientY, fromIndex) {
    if (!listRef.current) return null;
    const items = listRef.current.querySelectorAll('li');
    for (let i = 0; i < items.length; i++) {
      const rect = items[i].getBoundingClientRect();
      const mid = rect.top + rect.height / 2;
      if (clientY < mid) {
        // Insert before item i
        return i === fromIndex || i === fromIndex + 1 ? null : i;
      }
    }
    // Below last item
    const last = items.length;
    return last === fromIndex || last === fromIndex + 1 ? null : last;
  }

  function handleDragStart(index, e) {
    setDragFrom(index);
    e.dataTransfer.effectAllowed = "move";
  }

  function handleDragOver(e) {
    e.preventDefault();
    if (dragFrom === null) return;
    setDropTarget(calcDropTarget(e.clientY, dragFrom));
  }

  async function handleDragDrop(e) {
    e.preventDefault();
    if (dragFrom !== null && dropTarget !== null) {
      const to = dropTarget > dragFrom ? dropTarget - 1 : dropTarget;
      if (to !== dragFrom) {
        await connection.invoke("ReorderPlayer", roomId, dragFrom, to);
      }
    }
    setDragFrom(null);
    setDropTarget(null);
  }

  function handleDragEnd() {
    setDragFrom(null);
    setDropTarget(null);
  }

  // Touch drag support
  const touchFromRef = useRef(null);

  function handleTouchStart(index) {
    touchFromRef.current = index;
    setDragFrom(index);
  }

  function handleTouchMove(e) {
    if (touchFromRef.current === null) return;
    e.preventDefault();
    const touch = e.touches[0];
    setDropTarget(calcDropTarget(touch.clientY, touchFromRef.current));
  }

  async function handleTouchEnd() {
    if (touchFromRef.current !== null && dropTarget !== null) {
      const to = dropTarget > touchFromRef.current ? dropTarget - 1 : dropTarget;
      if (to !== touchFromRef.current) {
        await connection.invoke("ReorderPlayer", roomId, touchFromRef.current, to);
      }
    }
    touchFromRef.current = null;
    setDragFrom(null);
    setDropTarget(null);
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
    const name = gameState?.playerNames?.[targetIndex] || `Player ${targetIndex}`;
    if (!window.confirm(`Assassinate ${name}?`)) return;
    await connection.invoke("Assassinate", roomId, targetIndex);
  }
  async function handleEarlyAssassinate() {
    if (!window.confirm("Are you sure you want to assassinate Merlin now? This will end the current game immediately!")) return;
    await connection.invoke("EarlyAssassinate", roomId);
  }

  function toggleTeamMember(index) {
    setSelectedTeam((prev) =>
      prev.includes(index) ? prev.filter((i) => i !== index) : [...prev, index]
    );
  }

  function renderPlayerList() {
    if (roomPlayers.length === 0) return null;
    return (
      <ul
        className="av-player-list"
        ref={listRef}
        onDragOver={isHost ? handleDragOver : undefined}
        onDrop={isHost ? handleDragDrop : undefined}
        onTouchMove={isHost ? handleTouchMove : undefined}
        onTouchEnd={isHost ? handleTouchEnd : undefined}
      >
        {roomPlayers.map((p, i) => (
          <li
            key={i}
            draggable={isHost}
            onDragStart={isHost ? (e) => handleDragStart(i, e) : undefined}
            onDragEnd={isHost ? handleDragEnd : undefined}
            onTouchStart={isHost ? () => handleTouchStart(i) : undefined}
            className={[
              isHost ? "draggable" : "",
              dragFrom === i ? "dragging" : "",
              dropTarget === i ? "drop-before" : "",
              dropTarget === i + 1 && i === roomPlayers.length - 1 ? "" : "",
            ].join(" ")}
          >
            <span className="seat-number">{i + 1}.</span>
            {isHost && <span className="drag-handle">&#9776;</span>}
            {p.nickname} {p.isHost ? "\uD83D\uDC51" : p.isReady ? "\u2713" : "\u2014"}
            {isHost && !p.isHost && (
              <span className="player-actions">
                <button className="kick-btn" onClick={() => handleKick(p.seatIndex)}>Kick</button>
              </span>
            )}
          </li>
        ))}
        {dropTarget === roomPlayers.length && <li className="drop-indicator-last" />}
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
              <div key={pi} className={`history-proposal ${p.approved === true ? "approved" : p.approved === false ? "rejected" : ""}`}>
                <div className="proposal-header">
                  <span className="proposal-label">#{pi + 1}</span>
                  <span className="proposal-sub">Proposed by</span>
                  <span className="proposal-leader">{gameState.playerNames[p.leaderIndex]}</span>
                  {p.approved != null && (p.approved
                    ? <span className="badge-approve">Approved</span>
                    : <span className="badge-reject">Rejected</span>)}
                </div>
                <div className="proposal-team-row">
                  <span className="proposal-sub">Team</span>
                  <div className="proposal-team">
                    {p.team.map((t) => (
                      <span key={t} className="team-member">{gameState.playerNames[t]}</span>
                    ))}
                  </div>
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
                  <div className={`proposal-mission-result ${p.missionResult === "Success" ? "mission-success" : "mission-fail"}`}>
                    <span className="mission-result-counts">
                      <span className="count-success">{p.successCount} Success</span>
                      <span className="count-divider">/</span>
                      <span className="count-fail">{p.failCount} Fail</span>
                    </span>
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

      {myIndex === gs.assassinIndex && gs.phase !== "Assassination" && gs.phase !== "GameOver" && (
        <div className="early-assassinate-bar">
          <button className="btn-early-assassinate" onClick={handleEarlyAssassinate}>Assassinate Merlin</button>
        </div>
      )}

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
          <div className="action-panel action-highlight">
            <p className="action-title">Your turn! Select {gs.requiredTeamSize} players ({selectedTeam.length}/{gs.requiredTeamSize})</p>
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
          <div className="action-panel action-highlight-danger">
            {gs.bonusAssassination
              ? <p className="action-title">Evil won 3 missions! Find Merlin for double points! Click on your target.</p>
              : gs.earlyAssassination
              ? <p className="action-title">Choose your target! Click on who you think is Merlin!</p>
              : <p className="action-title">Good won 3 missions. Click on who you think is Merlin!</p>
            }
          </div>
        )}
        {gs.phase === "Assassination" && myIndex !== gs.assassinIndex && (
          <p className="waiting">
            {gs.bonusAssassination
              ? "Evil won 3 missions. Assassin is attempting to find Merlin for double points..."
              : gs.earlyAssassination
              ? "Assassin is attempting to assassinate Merlin..."
              : "Assassin is choosing a target..."}
          </p>
        )}

        {gs.phase === "GameOver" && (
          <div className="game-over">
            <h2 className={gs.winner === "Good" ? "good-win" : "evil-win"}>
              {gs.winReason?.includes("Double points")
                ? "Evil Epic Victory!!"
                : gs.winner === "Good" ? "Good Wins!" : "Evil Wins!"}
            </h2>
            <p className="win-reason">{gs.winReason}</p>
            {gs.assassinTarget != null && (
              <p className="assassin-target">Assassin targeted: <strong>{gs.playerNames[gs.assassinTarget]}</strong></p>
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
