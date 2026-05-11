import "./Home.css";

const GAMES = [
  {
    id: "blackjack",
    name: "BlackJack",
    description: "Classic casino card game. Beat the dealer by getting closer to 21 without going over.",
    icon: "\u2660",
  },
];

export default function Home({ nickname, onSelectGame, onProfile, onLogout }) {
  return (
    <div className="home-container">
      <div className="home-header">
        <h2>Welcome, {nickname}!</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          <button onClick={onProfile}>Profile</button>
          <button onClick={onLogout} style={{ background: '#94a3b8' }}>Logout</button>
        </div>
      </div>
      <p className="home-subtitle">Choose a game to play</p>
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
