import { useState, useEffect } from "react";
import { getAvalonGameDetail } from "../../services/api";
import "./AvalonGame.css";

const ROLE_LABELS = {
  Merlin: { name: "Merlin", emoji: "🧙" },
  Percival: { name: "Percival", emoji: "💂" },
  LoyalServant: { name: "Loyal Servant", emoji: "🛡️" },
  Assassin: { name: "Assassin", emoji: "🗡️" },
  Morgana: { name: "Morgana", emoji: "💋" },
  Mordred: { name: "Mordred", emoji: "😈" },
  Oberon: { name: "Oberon", emoji: "🤡" },
  MinionOfMordred: { name: "Minion", emoji: "🥷" },
};

const MISSION_SIZES = {
  5: [2, 3, 2, 3, 3],
  6: [2, 3, 4, 3, 4],
  7: [2, 3, 3, 4, 4],
  8: [3, 4, 4, 5, 5],
  9: [3, 4, 4, 5, 5],
  10: [3, 4, 4, 5, 5],
};
const isProtectedMission = (playerCount, i) => i === 3 && playerCount >= 7;
const TOTAL_MISSIONS = 5;

function teamForRole(role) {
  return ["Merlin", "Percival", "LoyalServant"].includes(role) ? "Good" : "Evil";
}

function formatDuration(seconds) {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return m > 0 ? `${m}m ${s}s` : `${s}s`;
}

export default function AvalonGameDetail({ gameId, onBack, fetchDetail }) {
  const [game, setGame] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await (fetchDetail ? fetchDetail(gameId) : getAvalonGameDetail(gameId));
        if (cancelled) return;
        if (data == null) setError("Game not found or you did not participate in it.");
        else setGame(data);
      } catch (e) {
        if (!cancelled) setError(e.message || "Failed to load");
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [gameId]);

  if (loading) {
    return <p className="text-muted" style={{ textAlign: "center", marginTop: 32 }}>Loading...</p>;
  }
  if (error || !game) {
    return (
      <div className="av-container">
        <button className="btn-small" onClick={onBack}>← Back</button>
        <p className="error-msg" style={{ marginTop: 16 }}>{error || "Not found"}</p>
      </div>
    );
  }

  // Sort players by seat so playerNames[i] etc. work uniformly
  const playersBySeat = [...game.players].sort((a, b) => a.seatIndex - b.seatIndex);
  const playerNames = playersBySeat.map((p) => p.nickname);
  const allRoles = playersBySeat.map((p) => p.role);

  // Build 5-mission results array from missions data
  const missionSizes = MISSION_SIZES[game.playerCount] || [];
  const missionResults = [];
  for (let i = 0; i < TOTAL_MISSIONS; i++) {
    const m = game.missions.find((x) => x.missionIndex === i);
    const deciding = m?.proposals.find((p) => p.missionResult === "Success" || p.missionResult === "Fail");
    missionResults.push(deciding?.missionResult || "Pending");
  }

  const iWon = game.myIsWinner;
  const isDoublePoints = !!game.winReason?.includes("Double points");
  const mainText = isDoublePoints
    ? (iWon ? "EPIC VICTORY!" : "CRUSHING DEFEAT!")
    : (iWon ? "YOU WIN" : "YOU LOSE");
  const subtitleText = isDoublePoints ? (iWon ? "YOU WIN" : "YOU LOSE") : null;
  const gameoverBg =
    game.winner === "Good" ? "/good-win.png?v=2" :
    isDoublePoints ? "/evil-epic-win.png?v=2" :
    "/evil-normal-win.png?v=2";
  const detailsBgStyle = {
    backgroundImage: `linear-gradient(180deg, rgba(0,0,0,0.15), rgba(0,0,0,0.25)), url('${gameoverBg}')`,
    backgroundSize: 'cover',
    backgroundPosition: 'center',
    backgroundRepeat: 'no-repeat',
    backgroundColor: '#1a1633',
  };

  return (
    <div className="av-container">
      <div className="av-header">
        <button className="btn-small" onClick={onBack}>← Back</button>
        <span className="room-info">
          {game.playerCount} players · {formatDuration(game.durationSeconds)} · Room {game.roomId}
        </span>
      </div>

      <div className="game-over">
        <div className="gameover-details" style={detailsBgStyle}>
          <h2 className={`gameover-title ${iWon ? "win" : "lose"} ${isDoublePoints ? "epic" : ""}`}>
            {subtitleText && <span className="gameover-title-subtitle">{subtitleText}</span>}
            <span className="gameover-title-main">{mainText}</span>
          </h2>
          <p className="win-reason">{game.winReason}</p>

          <div className="mission-track" style={{ justifyContent: "center", margin: "12px 0" }}>
            {missionResults.map((r, i) => {
              const protectedRound = isProtectedMission(game.playerCount, i);
              const isSuccess = r === "Success";
              const isFail = r === "Fail";
              return (
                <div
                  key={i}
                  className={`mission-dot ${isSuccess ? "success" : isFail ? "fail" : ""} ${protectedRound ? "protected" : ""}`}
                  title={protectedRound ? "Two fails needed to fail this mission" : `Mission ${i + 1}: ${missionSizes[i] || ""} players`}
                >
                  {isSuccess && <img src="/success_icon.png?v=2" alt="Success" className="mission-icon" />}
                  {isFail && <img src="/fail_icon.png?v=2" alt="Fail" className="mission-icon" />}
                  {!isSuccess && !isFail && (missionSizes[i] ?? (i + 1))}
                </div>
              );
            })}
          </div>

          {game.assassinTargetSeat != null && (
            <p className="assassin-target">
              Assassin targeted: <strong>{playerNames[game.assassinTargetSeat]}</strong>
            </p>
          )}
          <div className="roles-reveal">
            {allRoles.map((role, i) => {
              const p = playersBySeat[i];
              const won = p.balanceDelta >= 0;
              return (
                <div key={i} className={`role-reveal ${teamForRole(role) === "Evil" ? "evil" : "good"}`}>
                  <strong>{playerNames[i]}</strong>: {ROLE_LABELS[role]?.emoji} {ROLE_LABELS[role]?.name || role}
                  <span style={{
                    marginLeft: 8,
                    fontWeight: 800,
                    fontFamily: 'Georgia, "Times New Roman", serif',
                    color: won ? "#ffd27a" : "#8a1818",
                    textShadow: won
                      ? '0 0 6px rgba(255, 210, 120, 0.6), 0 1px 2px rgba(0, 0, 0, 0.7)'
                      : '0 1px 0 rgba(255, 245, 215, 0.6)'
                  }}>
                    {won ? "+" : ""}{p.balanceDelta}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      </div>

      <div className="history-panel">
        <h3 className="history-title">History</h3>
        {game.missions.map((mission) => (
          <div key={mission.missionIndex} className="history-mission">
            <h4 className="history-mission-title">Mission {mission.missionIndex + 1}</h4>
            {mission.proposals.length === 0 && <p className="history-empty">No proposals</p>}
            {mission.proposals.map((p) => (
              <div
                key={p.proposalIndex}
                className={`history-proposal ${p.approved === true ? "approved" : p.approved === false ? "rejected" : ""}`}
              >
                <div className="proposal-header">
                  <span className="proposal-label">#{p.proposalIndex + 1}</span>
                  <img src="/leader.png?v=2" alt="" className="history-inline-icon" />
                  <span className="proposal-leader">{playerNames[p.leaderSeatIndex]}</span>
                  {p.approved != null && (
                    <img
                      src={p.approved ? "/approve.png?v=2" : "/reject.png?v=2"}
                      alt={p.approved ? "Approved" : "Rejected"}
                      className="history-result-icon"
                    />
                  )}
                </div>
                <div className="proposal-team-row">
                  <div className="proposal-team">
                    {p.teamSeats.map((t) => (
                      <span key={t} className="history-chip history-team-chip">
                        <img src="/team.png?v=2" alt="" className="history-chip-shield" />
                        {playerNames[t]}
                      </span>
                    ))}
                  </div>
                </div>
                {p.votes && p.votes.length > 0 && (
                  <div className="proposal-votes">
                    {p.votes.map((v) => (
                      <span key={v.voterSeatIndex} className={`history-chip vote-chip ${v.approve ? "approve" : "reject"}`}>
                        {playerNames[v.voterSeatIndex]}
                      </span>
                    ))}
                  </div>
                )}
                {p.missionResult && p.missionResult !== "Pending" && (
                  <div className="proposal-mission-result">
                    {[...Array(p.successCount)].map((_, i) => (
                      <img key={`s${i}`} src="/success_icon.png?v=2" alt="Success" className="history-mission-dot" />
                    ))}
                    {[...Array(p.failCount)].map((_, i) => (
                      <img key={`f${i}`} src="/fail_icon.png?v=2" alt="Fail" className="history-mission-dot" />
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}
