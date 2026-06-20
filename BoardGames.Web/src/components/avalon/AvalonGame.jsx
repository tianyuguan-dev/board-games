import { useState, useEffect, useRef } from "react";
import "./AvalonGame.css";

const TOGGLE_EVIL = ["Mordred", "Oberon"];

// Mission team sizes per player count (mirrors AvalonConfig.MissionSizes on the backend).
const MISSION_SIZES = {
  5: [2, 3, 2, 3, 3],
  6: [2, 3, 4, 3, 4],
  7: [2, 3, 3, 4, 4],
  8: [3, 4, 4, 5, 5],
  9: [3, 4, 4, 5, 5],
  10: [3, 4, 4, 5, 5],
};
// 7+ players: mission 4 (index 3) needs two fails to fail — the "protected" round.
const isProtectedMission = (playerCount, i) => i === 3 && playerCount >= 7;

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

const ROLE_IMAGES = {
  Merlin: "/Merlin.png",
  Percival: "/Percival.jpeg",
  LoyalServant: "/Loyal_Servant_of_Arthur_clean.png",
  Assassin: "/Assassin.png",
  Morgana: "/Morgana.jpeg",
  Mordred: "/Mordred.png",
  Oberon: "/Oberon.png",
  MinionOfMordred: "/Minion_of_Mordred_clean.png",
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

// Get the most recent proposal across all missions (flattened from history)
function getLastProposal(state) {
  if (!state?.history) return null;
  for (let i = state.history.length - 1; i >= 0; i--) {
    const props = state.history[i];
    if (props && props.length > 0) return props[props.length - 1];
  }
  return null;
}

// Total number of proposals across all missions
function countProposals(state) {
  if (!state?.history) return 0;
  return state.history.reduce((sum, props) => sum + (props?.length ?? 0), 0);
}

export default function AvalonGame({ connection, nickname, isGuest, roomId, maxPlayers, playerCount, roomPlayers, mySeatIndex, isHost, roleConfig, maxRejects, needsRejoin, gameInProgress, onLeave }) {
  const [gameState, setGameState] = useState(null);
  const [myIndex, setMyIndex] = useState(-1);
  // Use lobby-time mySeatIndex (from RoomUpdate) when game hasn't started yet,
  // fall back to game-time myIndex once YourSeat / GameState arrives.
  const effectiveIndex = myIndex >= 0 ? myIndex : (mySeatIndex ?? -1);
  // ready is derived from server state to avoid desync after reconnect / game abort
  const ready = roomPlayers.find((p) => p.seatIndex === effectiveIndex)?.isReady ?? false;
  const [selectedTeam, setSelectedTeam] = useState([]);
  const [nightConfirmed, setNightConfirmed] = useState(false);
  const [hasVoted, setHasVoted] = useState(false);
  const [hasPlayedCard, setHasPlayedCard] = useState(false);
  const [balance, setBalance] = useState(null);
  const [playersWhoVoted, setPlayersWhoVoted] = useState([]);
  const [missionPlayersPlayed, setMissionPlayersPlayed] = useState([]);
  const [nightConfirmedPlayers, setNightConfirmedPlayers] = useState([]);
  const [infoRevealed, setInfoRevealed] = useState(false);
  // Result animations: shown briefly when vote/mission resolves
  const [voteAnimation, setVoteAnimation] = useState(null);     // { approved, votes, leaderName, team }
  const [missionAnimation, setMissionAnimation] = useState(null); // { success, successCount, failCount, missionIndex }

  useEffect(() => {
    connection.on("YourSeat", (index) => setMyIndex(index));
    connection.on("GameState", (state) => {
      // Detect vote/mission resolution by comparing with previous state
      setGameState((prev) => {
        // Vote just resolved: a new proposal was appended to history
        const prevCount = countProposals(prev);
        const newCount = countProposals(state);
        if (newCount > prevCount) {
          const newLast = getLastProposal(state);
          if (newLast && newLast.approved != null) {
            const leaderName = state.playerNames?.[newLast.leaderIndex] ?? "Leader";
            setVoteAnimation({
              approved: newLast.approved,
              votes: newLast.votes,
              leaderName,
              team: newLast.team,
              playerNames: state.playerNames,
            });
            setTimeout(() => setVoteAnimation(null), 1500);
          }
        }
        // Mission just resolved: same last proposal's missionResult flipped null → Success/Fail
        const prevLast = getLastProposal(prev);
        const newLast = getLastProposal(state);
        if (prevLast && newLast && prevLast.missionResult == null && newLast.missionResult != null) {
          setMissionAnimation({
            success: newLast.missionResult === "Success",
            successCount: newLast.successCount ?? 0,
            failCount: newLast.failCount ?? 0,
            missionIndex: state.currentMissionIndex,
          });
          setTimeout(() => setMissionAnimation(null), 1500);
        }
        return state;
      });
      setMyIndex(state.myIndex);
      setNightConfirmed(false);
      setSelectedTeam([]);
      // Restore vote/mission state on reconnect
      const mi = state.myIndex;
      setHasVoted(!!(state.phase === "TeamVote" && state.playersWhoVoted?.includes(mi)));
      setHasPlayedCard(!!(state.phase === "Mission" && state.missionPlayersPlayed?.includes(mi)));
      setPlayersWhoVoted(state.playersWhoVoted || []);
      setMissionPlayersPlayed(state.missionPlayersPlayed || []);
      setNightConfirmedPlayers([]);
      setInfoRevealed(false);
    });
    connection.on("VoteProgress", (voted) => setPlayersWhoVoted(voted));
    connection.on("MissionProgress", (played) => setMissionPlayersPlayed(played));
    connection.on("NightRevealProgress", (confirmed) => setNightConfirmedPlayers(confirmed));
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
    try { await connection.invoke("Ready", roomId); }
    catch (e) { alert(e.message); }
  }
  async function handleUnready() {
    try { await connection.invoke("Unready", roomId); }
    catch {}
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
    try { await connection.invoke("KickPlayer", roomId, seatIndex); }
    catch (e) { alert(e.message); }
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
        try { await connection.invoke("ReorderPlayer", roomId, dragFrom, to); } catch {}
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
        try { await connection.invoke("ReorderPlayer", roomId, touchFromRef.current, to); } catch {}
      }
    }
    touchFromRef.current = null;
    setDragFrom(null);
    setDropTarget(null);
  }
  async function handleLeave() {
    if (gameState && gameState.phase !== "GameOver") {
      if (!window.confirm("Leaving during a game will abort the game. Are you sure?")) {
        return;
      }
    }
    try { await connection.invoke("LeaveRoom"); } catch {}
    onLeave();
  }
  async function handleConfirmNight() {
    try {
      setNightConfirmed(true);
      await connection.invoke("ConfirmNightReveal", roomId);
      setNightConfirmedPlayers((prev) => prev.includes(myIndex) ? prev : [...prev, myIndex]);
    }
    catch { setNightConfirmed(false); }
  }
  async function handleProposeTeam() {
    try { await connection.invoke("ProposeTeam", roomId, selectedTeam); }
    catch (e) { alert(e.message); }
  }
  async function handleVote(approve) {
    if (!approve && gameState && gameState.consecutiveRejects >= (gameState.maxConsecutiveRejects || 5) - 1) {
      if (!window.confirm("This is the last chance! If this proposal is rejected, the good side loses immediately. Are you sure?")) {
        return;
      }
    }
    try { await connection.invoke("CastVote", roomId, approve); setHasVoted(true); }
    catch (e) { alert(e.message); }
  }
  async function handleMissionCard(success) {
    try { await connection.invoke("PlayMissionCard", roomId, success); setHasPlayedCard(true); }
    catch (e) { alert(e.message); }
  }
  async function handleAssassinate(targetIndex) {
    const name = gameState?.playerNames?.[targetIndex] || `Player ${targetIndex}`;
    if (!window.confirm(`Assassinate ${name}?`)) return;
    try { await connection.invoke("Assassinate", roomId, targetIndex); }
    catch (e) { alert(e.message); }
  }
  async function handleEarlyAssassinate() {
    if (!window.confirm("Are you sure you want to assassinate Merlin now? This will end the current game immediately!")) return;
    try { await connection.invoke("EarlyAssassinate", roomId); }
    catch (e) { alert(e.message); }
  }

  function toggleTeamMember(index) {
    setSelectedTeam((prev) =>
      prev.includes(index) ? prev.filter((i) => i !== index) : [...prev, index]
    );
  }

  function renderAnimations() {
    return (
      <>
        {voteAnimation && (
          <div className={`vote-anim-overlay ${voteAnimation.approved ? "approve" : "reject"}`}>
            <div className="vote-anim-card">
              <div className="vote-anim-title">
                {voteAnimation.approved ? "✓ Team Approved" : "✗ Team Rejected"}
              </div>
              <div className="vote-anim-leader">
                Proposed by <strong>{voteAnimation.leaderName}</strong>
              </div>
              <div className="vote-anim-team">
                {voteAnimation.team?.map((idx) => (
                  <span key={idx} className="vote-anim-team-name">
                    {voteAnimation.playerNames?.[idx] || `P${idx + 1}`}
                  </span>
                ))}
              </div>
              {voteAnimation.votes && (
                <div className="vote-anim-votes">
                  {Object.entries(voteAnimation.votes).map(([idx, approved]) => (
                    <span key={idx} className={`vote-anim-vote ${approved ? "yes" : "no"}`}>
                      {voteAnimation.playerNames?.[idx] || `P${Number(idx) + 1}`}
                      <span className="vote-anim-mark">{approved ? "👍" : "👎"}</span>
                    </span>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}
        {missionAnimation && (
          <div className={`mission-anim-overlay ${missionAnimation.success ? "success" : "fail"}`}>
            <div className="mission-anim-card">
              <div className="mission-anim-title">
                {missionAnimation.success ? "🏆 Mission Succeeded" : "💥 Mission Failed"}
              </div>
              <div className="mission-anim-counts">
                <span className="mission-anim-success">
                  ✅ {missionAnimation.successCount} Success
                </span>
                <span className="mission-anim-fail">
                  ❌ {missionAnimation.failCount} Fail
                </span>
              </div>
            </div>
          </div>
        )}
      </>
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
    const sizes = MISSION_SIZES[gameState.playerCount] || [];
    const gs = gameState;
    const myRole = ROLE_LABELS[gs.myRole] || { name: gs.myRole, emoji: "", desc: "" };
    const isEvil = gs.myTeam === "Evil";
    const isGameOver = gs.phase === "GameOver";
    const showIdentity = infoRevealed && !isGameOver;
    const clickable = !isGameOver;

    return (
      <div
        className={`scoreboard ${showIdentity ? "scoreboard-identity" : ""} ${clickable ? "scoreboard-clickable" : ""}`}
        onClick={clickable ? () => setInfoRevealed((v) => !v) : undefined}
      >
        {clickable && (
          <div className="scoreboard-toggle-hint">
            {showIdentity ? "Tap to view scoreboard" : "Tap to reveal your role & info"}
          </div>
        )}
        {showIdentity ? (
          <div className="scoreboard-identity-content">
            <div className="identity-split">
              {ROLE_IMAGES[gs.myRole] ? (
                <img
                  className="identity-portrait"
                  src={ROLE_IMAGES[gs.myRole]}
                  alt={myRole.name}
                />
              ) : (
                <div className="identity-portrait identity-portrait-fallback">
                  <span className="identity-emoji">{myRole.emoji}</span>
                </div>
              )}
              <div className="identity-text">
                <div className="identity-role-row">
                  <span className="identity-name">{myRole.name}</span>
                  <span className={`identity-team ${isEvil ? "evil" : "good"}`}>{gs.myTeam}</span>
                </div>
                <p className="identity-desc">{myRole.desc}</p>
                {gs.visiblePlayers && gs.visiblePlayers.length > 0 && (
                  <div className="identity-info">
                    <span className="identity-hint">{gs.visibleHint}:</span>
                    {gs.visiblePlayers.map((i) => (
                      <span key={i} className="identity-player">
                        {gs.playerNames[i]}
                        {gs.visiblePlayerRoles && gs.visiblePlayerRoles[i] && (
                          <span className="identity-role-tag"> ({ROLE_LABELS[gs.visiblePlayerRoles[i]]?.name || gs.visiblePlayerRoles[i]})</span>
                        )}
                      </span>
                    ))}
                  </div>
                )}
                {myIndex === gs.assassinIndex && gs.phase !== "Assassination" && (
                  <div style={{ marginTop: 10 }}>
                    <button
                      className="btn-early-assassinate"
                      onClick={(e) => { e.stopPropagation(); handleEarlyAssassinate(); }}
                    >
                      Assassinate Merlin
                    </button>
                  </div>
                )}
              </div>
            </div>
          </div>
        ) : (
          <>
            <div className="mission-track">
              {gs.missionResults.map((r, i) => {
                const protectedRound = isProtectedMission(gs.playerCount, i);
                const isSuccess = r === "Success";
                const isFail = r === "Fail";
                return (
                  <div
                    key={i}
                    className={`mission-dot ${isSuccess ? "success" : isFail ? "fail" : ""} ${i === gs.currentMissionIndex ? "current" : ""} ${protectedRound ? "protected" : ""}`}
                    title={protectedRound ? "Two fails needed to fail this mission" : `Mission ${i + 1}: ${sizes[i] || ""} players`}
                  >
                    {isSuccess && <img src="/success_icon.png" alt="Success" className="mission-icon" />}
                    {isFail && <img src="/fail_icon.png" alt="Fail" className="mission-icon" />}
                    {!isSuccess && !isFail && (sizes[i] ?? (i + 1))}
                  </div>
                );
              })}
            </div>
            <div className="reject-track">
              <span className="reject-label">Vote Track</span>
              <div className="reject-dots">
                {[...Array(gs.maxConsecutiveRejects || 5)].map((_, i) => (
                  <span key={i} className={`reject-dot ${i < gs.consecutiveRejects ? "active" : ""}`} />
                ))}
              </div>
            </div>
            {roleConfig && roleConfig.length > 0 && (() => {
              const evilRoles = roleConfig.filter((r) => !["Merlin", "Percival", "LoyalServant"].includes(r));
              if (evilRoles.length === 0) return null;
              return (
                <div className="evil-roles-display">
                  {groupRoles(evilRoles).map(({ role, count }) => (
                    <span key={role} className="evil-role-chip">
                      <span className="evil-role-emoji">{ROLE_LABELS[role]?.emoji}</span>
                      {ROLE_LABELS[role]?.name || role}{count > 1 ? ` ×${count}` : ""}
                    </span>
                  ))}
                </div>
              );
            })()}
          </>
        )}
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
    const canStart = roomPlayers.length === maxPlayers &&
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
            {ROLE_IMAGES[gs.myRole] ? (
              <img
                className="role-image"
                src={ROLE_IMAGES[gs.myRole]}
                alt={myRole.name}
              />
            ) : (
              <span className="role-emoji">{myRole.emoji}</span>
            )}
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
          <div className="progress-status" style={{ marginTop: 16 }}>
            {gs.playerNames.map((name, i) => (
              <span key={i} className={`status-chip ${nightConfirmedPlayers.includes(i) ? "acted" : "pending"}`}>
                {name} {nightConfirmedPlayers.includes(i) ? "●" : "…"}
              </span>
            ))}
          </div>
        </div>
        {renderAnimations()}
      </div>
    );
  }

  // Game phases
  return (
    <div className="av-container">
      <div className="av-header">
        <span className="room-info">Room {roomId}{balance !== null && ` | ${nickname || "You"}'s net wins: ${balance}`}</span>
        <button className="btn-small" style={{ color: "#dc2626" }} onClick={handleLeave}>Leave</button>
      </div>
      {renderAnimations()}

      {renderMissionTrack()}

      {/* Player circle */}
      <div className="av-players">
        {gs.playerNames.map((name, i) => {
          const onTeam = gs.proposedTeam.includes(i);
          const isCurrLeader = i === gs.currentLeaderIndex;
          const isMe = i === myIndex;
          const selectable = gs.phase === "TeamProposal" && isLeader;
          const selected = selectedTeam.includes(i);
          const isAssassinPhase = gs.phase === "Assassination" && myIndex === gs.assassinIndex;
          const canAssassinate = isAssassinPhase && gs.assassinationTargets?.includes(i);
          const isAlly = isAssassinPhase && !gs.assassinationTargets?.includes(i) && i !== myIndex;
          return (
            <div
              key={i}
              className={`av-player ${isMe ? "me" : ""} ${onTeam ? "on-team" : ""} ${isCurrLeader ? "leader" : ""} ${selected ? "selected" : ""} ${selectable || canAssassinate ? "clickable" : ""} ${isAlly ? "ally-disabled" : ""}`}
              onClick={() => {
                if (selectable) toggleTeamMember(i);
                if (canAssassinate) handleAssassinate(i);
              }}
            >
              <span className="player-name">
                {name}
                {isCurrLeader && <img src="/leader.png" alt="Leader" className="leader-icon" />}
                {(onTeam || selected) && <img src="/team.png" alt="On team" className="team-icon" />}
              </span>
              {isMe && <span className="me-tag">(You)</span>}
              {isAlly && <span className="ally-tag">Ally</span>}
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
            <div className="proposed-team-display">
              <span className="proposed-team-label">Team</span>
              <div className="proposed-team-members">
                {gs.proposedTeam.map((i) => (
                  <span key={i} className="proposed-team-member">
                    <img src="/team.png" alt="" className="proposed-team-icon" />
                    {gs.playerNames[i]}
                  </span>
                ))}
              </div>
            </div>
            <div className="progress-status">
              {gs.playerNames.map((name, i) => (
                <span key={i} className={`status-chip ${playersWhoVoted.includes(i) ? "acted" : "pending"}`}>
                  {name} {playersWhoVoted.includes(i) ? "●" : "…"}
                </span>
              ))}
            </div>
            {hasVoted ? (
              <p className="waiting">Voted! Waiting for others...</p>
            ) : (
              <div className="vote-btn-row">
                <button
                  className="vote-img-btn vote-approve"
                  onClick={() => handleVote(true)}
                  aria-label="Approve team"
                >
                  <img src="/approve.png" alt="Approve" />
                </button>
                <button
                  className="vote-img-btn vote-reject"
                  onClick={() => handleVote(false)}
                  aria-label="Reject team"
                >
                  <img src="/reject.png" alt="Reject" />
                </button>
              </div>
            )}
          </div>
        )}

        {gs.phase === "Mission" && (
          <div className="action-panel">
            <div className="progress-status">
              {gs.proposedTeam?.map((i) => (
                <span key={i} className={`status-chip ${missionPlayersPlayed.includes(i) ? "acted" : "pending"}`}>
                  {gs.playerNames[i]} {missionPlayersPlayed.includes(i) ? "●" : "…"}
                </span>
              ))}
            </div>
            {gs.proposedTeam?.includes(myIndex) ? (
              hasPlayedCard ? (
                <p className="waiting">Card played! Waiting for others...</p>
              ) : (
                <>
                  <p>You're on the mission! Play your card:</p>
                  <div className="vote-btn-row">
                    <button
                      className="vote-img-btn vote-approve"
                      onClick={() => handleMissionCard(true)}
                      aria-label="Play Success card"
                    >
                      <img src="/success.png" alt="Success" />
                    </button>
                    <button
                      className="vote-img-btn vote-reject"
                      onClick={() => handleMissionCard(false)}
                      disabled={!isEvil}
                      title={isEvil ? "" : "Good players can only play Success"}
                      aria-label="Play Fail card"
                    >
                      <img src="/fail.png" alt="Fail" />
                    </button>
                  </div>
                </>
              )
            ) : (
              <p className="waiting">Mission in progress...</p>
            )}
          </div>
        )}

        {gs.phase === "Assassination" && myIndex === gs.assassinIndex && (
          <div className="action-panel action-highlight-danger">
            {gs.bonusAssassination
              ? <p className="action-title">Evil already won ({gs.bonusLossReason})! Find Merlin for double points! Click on your target.</p>
              : gs.earlyAssassination
              ? <p className="action-title">Choose your target! Click on who you think is Merlin!</p>
              : <p className="action-title">Good won 3 missions. Click on who you think is Merlin!</p>
            }
          </div>
        )}
        {gs.phase === "Assassination" && myIndex !== gs.assassinIndex && (
          <div className="assassin-watching">
            <span className="assassin-watching-icon">🗡️</span>
            <span className="assassin-watching-text">
              {gs.bonusAssassination
                ? `Evil already won (${gs.bonusLossReason}). Assassin is hunting Merlin for double points...`
                : "Assassin is searching for Merlin..."}
            </span>
          </div>
        )}

        {gs.phase === "GameOver" && (() => {
          const myRole = gs.allRoles?.[myIndex];
          const myTeam = myRole ? AvalonTeamForRole(myRole) : null;
          const iWon = myTeam != null && myTeam === gs.winner;
          const isDoublePoints = !!gs.winReason?.includes("Double points");
          const mainText = isDoublePoints
            ? (iWon ? "EPIC VICTORY!" : "CRUSHING DEFEAT!")
            : (iWon ? "YOU WIN" : "YOU LOSE");
          const subtitleText = isDoublePoints ? (iWon ? "YOU WIN" : "YOU LOSE") : null;
          return (
            <div className="game-over">
              <div className={`gameover-banner ${iWon ? "win" : "lose"} ${isDoublePoints ? "epic" : ""}`}>
                {subtitleText && <span className="gameover-banner-subtitle">{subtitleText}</span>}
                <span className="gameover-banner-text">{mainText}</span>
              </div>
              <div className="gameover-details">
                <h2 className={`gameover-title ${iWon ? "win" : "lose"} ${isDoublePoints ? "epic" : ""}`}>
                  {subtitleText && <span className="gameover-title-subtitle">{subtitleText}</span>}
                  <span className="gameover-title-main">{mainText}</span>
                </h2>
                <p className="win-reason">{gs.winReason}</p>
                {gs.assassinTarget != null && (
                  <p className="assassin-target">Assassin targeted: <strong>{gs.playerNames[gs.assassinTarget]}</strong></p>
                )}
                <div className="roles-reveal">
                  {gs.allRoles.map((role, i) => {
                    const delta = gs.balanceDeltas?.[i];
                    const deltaShown = delta != null;
                    const won = deltaShown && delta >= 0;
                    return (
                      <div key={i} className={`role-reveal ${AvalonTeamForRole(role) === "Evil" ? "evil" : "good"}`}>
                        <strong>{gs.playerNames[i]}</strong>: {ROLE_LABELS[role]?.emoji} {ROLE_LABELS[role]?.name || role}
                        {deltaShown && (
                          <span style={{
                            marginLeft: 8,
                            fontWeight: 700,
                            color: won ? "#16a34a" : "#dc2626"
                          }}>
                            {won ? "+" : ""}{delta}
                          </span>
                        )}
                      </div>
                    );
                  })}
                </div>
                <div className="progress-status" style={{ marginTop: 16 }}>
                  {roomPlayers.map((p, i) => (
                    <span key={i} className={`status-chip ${p.isHost || p.isReady ? "acted" : "pending"}`}>
                      {p.nickname} {p.isHost ? "👑" : p.isReady ? "●" : "…"}
                    </span>
                  ))}
                </div>
              </div>
              <div className="btn-row gameover-actions" style={{ marginTop: 12 }}>
                {!isGuest && (
                  isHost ? (
                    <button className="btn btn-success" onClick={handleStart}
                      disabled={roomPlayers.length !== maxPlayers || (roomPlayers.filter((p) => !p.isHost).some((p) => !p.isReady) && roomPlayers.length > 1)}>
                      Start Next Round{roomPlayers.length !== maxPlayers ? ` (${roomPlayers.length}/${maxPlayers})` : ""}
                    </button>
                  ) : ready ? (
                    <button className="btn btn-warning" onClick={handleUnready}>Ready ✓</button>
                  ) : (
                    <button className="btn btn-success" onClick={handleReady}>Play Again</button>
                  )
                )}
              </div>
            </div>
          );
        })()}
      </div>

      {renderHistory()}
    </div>
  );
}

function AvalonTeamForRole(role) {
  return ["Merlin", "Percival", "LoyalServant"].includes(role) ? "Good" : "Evil";
}
