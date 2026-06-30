import { useState, useEffect } from "react";
import { getMyAvalonGames, localDayToUtcRange } from "../../services/api";
import DatePickerEN from "../DatePickerEN";
import "./AvalonHistory.css";

const ROLE_TEAM = {
  Merlin: "good", Percival: "good", LoyalServant: "good",
  Assassin: "evil", Morgana: "evil", Mordred: "evil", Oberon: "evil", MinionOfMordred: "evil",
};

const ROLE_LABELS = {
  Merlin: "Merlin", Percival: "Percival", LoyalServant: "Loyal Servant",
  Assassin: "Assassin", Morgana: "Morgana", Mordred: "Mordred",
  Oberon: "Oberon", MinionOfMordred: "Minion",
};

const PAGE_SIZE = 20;

function formatDate(iso) {
  const d = new Date(iso);
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export default function AvalonHistory({ onSelectGame, onBack }) {
  const [games, setGames] = useState([]);
  const [offset, setOffset] = useState(0);
  const [hasMore, setHasMore] = useState(true);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [date, setDate] = useState("");

  useEffect(() => {
    loadPage(0, true, "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function loadPage(off, replace, dateArg = date) {
    setLoading(true);
    setError("");
    try {
      const { from, to } = localDayToUtcRange(dateArg);
      const batch = await getMyAvalonGames(PAGE_SIZE, off, from, to);
      setGames(replace ? batch : [...games, ...batch]);
      setOffset(off + batch.length);
      setHasMore(batch.length === PAGE_SIZE);
    } catch (e) {
      setError(e.message || "Failed to load history");
    } finally {
      setLoading(false);
    }
  }

  function handleApplyFilter() {
    loadPage(0, true, date);
  }

  function handleClearFilter() {
    setDate("");
    loadPage(0, true, "");
  }

  return (
    <div className="page-center" style={{ maxWidth: 560 }}>
      <div className="av-history-header">
        <h2 style={{ margin: 0 }}>Game History</h2>
        <button className="btn-small" onClick={onBack}>← Back</button>
      </div>

      <div className="av-history-filter">
        <DatePickerEN value={date} onChange={setDate} />
        <button className="btn-small" onClick={handleApplyFilter} disabled={loading}>Apply</button>
        {date && (
          <button className="btn-small btn-secondary" onClick={handleClearFilter} disabled={loading}>Clear</button>
        )}
      </div>

      {error && <p className="error-msg">{error}</p>}

      {!loading && games.length === 0 && !error && (
        <p className="text-muted" style={{ textAlign: "center", marginTop: 32 }}>
          No games yet. Play one and come back!
        </p>
      )}

      <div className="av-history-list">
        {games.map((g) => {
          const team = ROLE_TEAM[g.myRole] || "good";
          const won = g.myIsWinner;
          const deltaSign = g.myBalanceDelta >= 0 ? "+" : "";
          return (
            <div
              key={g.id}
              className={`av-history-card ${won ? "won" : "lost"}`}
              onClick={() => onSelectGame(g.id)}
            >
              <div className="av-history-row1">
                <span className="av-date">{formatDate(g.endedAt)}</span>
                {g.isRanked === false && <span className="mode-badge casual">Casual</span>}
                <span className={`winner-badge ${g.winner.toLowerCase()}`}>
                  {g.winner === "Good" ? "Good Wins" : "Evil Wins"}
                </span>
              </div>
              <div className="av-history-row2">
                <span className="text-muted">{g.playerCount} players</span>
                <span className={`role-tag ${team}`}>
                  {ROLE_LABELS[g.myRole] || g.myRole}
                </span>
                <span className={`result-tag ${won ? "won" : "lost"}`}>
                  {won ? "Won" : "Lost"}{g.isRanked === false ? "" : ` ${deltaSign}${g.myBalanceDelta}`}
                </span>
              </div>
            </div>
          );
        })}
      </div>

      {hasMore && games.length > 0 && (
        <button
          onClick={() => loadPage(offset, false)}
          disabled={loading}
          style={{ marginTop: 12 }}
        >
          {loading ? "Loading..." : "Load more"}
        </button>
      )}

      {loading && games.length === 0 && (
        <p className="text-muted" style={{ textAlign: "center" }}>Loading...</p>
      )}
    </div>
  );
}
