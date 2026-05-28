import { useRef, useState, useEffect } from "react";

// Standard competition ranking: ties share a rank, the next distinct value skips (1, 1, 3).
function withRanks(entries) {
  let lastRank = 0, lastBalance = null;
  return entries.map((e, i) => {
    const rank = lastBalance !== null && e.balance === lastBalance ? lastRank : i + 1;
    lastRank = rank;
    lastBalance = e.balance;
    return { ...e, rank };
  });
}

export default function Leaderboard({ entries, nickname, valueLabel = "Balance" }) {
  const scrollRef = useRef(null);
  const [showMore, setShowMore] = useState(false);

  function update() {
    const el = scrollRef.current;
    if (!el) return;
    const canScroll = el.scrollHeight > el.clientHeight + 1;
    const atBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 1;
    setShowMore(canScroll && !atBottom);
  }

  useEffect(() => { update(); }, [entries]);

  const ranked = withRanks(entries);

  return (
    <div className="leaderboard-wrap">
      <div className="leaderboard-scroll" ref={scrollRef} onScroll={update}>
        <table className="leaderboard-table">
          <thead>
            <tr><th>#</th><th>Player</th><th>{valueLabel}</th></tr>
          </thead>
          <tbody>
            {ranked.map((entry, i) => (
              <tr key={i} className={entry.nickname === nickname ? "highlight" : ""}>
                <td>{entry.rank}</td>
                <td>{entry.nickname}</td>
                <td>{entry.balance}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {showMore && (
        <div className="leaderboard-fade">
          <span className="leaderboard-more">▾ Scroll for more</span>
        </div>
      )}
    </div>
  );
}
