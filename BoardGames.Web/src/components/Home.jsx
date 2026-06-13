import "./Home.css";

const GAMES = [
  {
    id: "avalon",
    name: "Avalon",
    description: "Social deduction game for 5-10 players. Find the spies, protect Merlin, complete the missions.",
    icon: "\uD83D\uDDE1\uFE0F",
  },
  {
    id: "blackjack",
    name: "BlackJack",
    description: "Classic casino card game. Beat the dealer by getting closer to 21 without going over.",
    icon: "\u2660",
  },
];

export default function Home({ nickname, isGuest, onSelectGame, onProfile, onLogout }) {
  return (
    <div className="home-container">
      <div className="home-header">
        <h2>Welcome, {nickname}!{isGuest && <span style={{ fontSize: 14, color: "#6366f1", marginLeft: 8 }}>(Guest)</span>}</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          {!isGuest && <button onClick={onProfile}>Profile</button>}
          <button onClick={onLogout} style={{ background: '#94a3b8' }}>Logout</button>
        </div>
      </div>
      <p className="home-subtitle">
        {isGuest ? "Guest mode: Avalon multiplayer is locked, but try the solo demo + BlackJack vs dealer." : "Choose a game to play"}
      </p>
      <div className="game-grid">
        {GAMES.map((game) => (
          <div key={game.id} className="game-card" onClick={() => onSelectGame(game.id)}>
            <span className="game-icon">{game.icon}</span>
            <h3>{game.name}</h3>
            <p>{game.description}</p>
          </div>
        ))}
      </div>
    </div>
  );
}
