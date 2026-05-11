import { useState, useEffect, useRef } from "react";
import "./Game.css";

const TURN_TIME = 10;
const BETTING_TIME = 10;
const MIN_BET = 1;
const MAX_BET = 100;

const SUITS = { 1: "\u2665", 2: "\u2660", 3: "\u2663", 4: "\u2666" };
const RANKS = {
  1: "A", 2: "2", 3: "3", 4: "4", 5: "5", 6: "6", 7: "7",
  8: "8", 9: "9", 10: "10", 11: "J", 12: "Q", 13: "K",
};

function Card({ card }) {
  const suit = SUITS[card.suit];
  const rank = RANKS[card.rank];
  const isRed = card.suit === 1 || card.suit === 4;
  return (
    <div className={`card ${isRed ? "red" : "black"}`}>
      <div className="card-corner top">
        <span className="card-rank">{rank}</span>
        <span className="card-suit">{suit}</span>
      </div>
      <span className="card-center">{suit}</span>
      <div className="card-corner bottom">
        <span className="card-rank">{rank}</span>
        <span className="card-suit">{suit}</span>
      </div>
    </div>
  );
}

function MiniCards({ cards }) {
  return (
    <span className="mini-cards">
      {cards.map((c, i) => {
        const isRed = c.suit === 1 || c.suit === 4;
        return (
          <span key={i} className={`mini-card ${isRed ? "red" : "black"}`}>
            {RANKS[c.rank]}{SUITS[c.suit]}
          </span>
        );
      })}
    </span>
  );
}

export default function Game({ connection, roomId, maxPlayers, playerCount, roomPlayers, isHost, onLeave }) {
  const [gameState, setGameState] = useState(null);
  const [ready, setReady] = useState(false);
  const [myIndex, setMyIndex] = useState(-1);
  const [timeLeft, setTimeLeft] = useState(TURN_TIME);
  const [balance, setBalance] = useState(null);
  const [betAmount, setBetAmount] = useState(10);
  const timerRef = useRef(null);

  function resetTimer(seconds) {
    clearInterval(timerRef.current);
    setTimeLeft(seconds);
    timerRef.current = setInterval(() => {
      setTimeLeft((t) => (t > 0 ? t - 1 : 0));
    }, 1000);
  }

  function stopTimer() {
    clearInterval(timerRef.current);
    timerRef.current = null;
  }

  useEffect(() => {
    connection.invoke("GetBalance").then(setBalance).catch(() => {});
    return () => clearInterval(timerRef.current);
  }, [connection]);

  useEffect(() => {
    function onGameState(state) {
      setGameState(state);
      if (state.state === 2) stopTimer();
      else if (state.state === 0) resetTimer(TURN_TIME);
    }

    connection.on("YourSeat", (index) => setMyIndex(index));
    connection.on("BalanceUpdate", (bal) => setBalance(bal));
    connection.on("StartGame", (state) => {
      setGameState(state);
      setReady(false);
      if (state.state === 3) resetTimer(BETTING_TIME);
      else if (state.state === 2) stopTimer();
      else resetTimer(TURN_TIME);
    });
    connection.on("BetPlaced", (state) => setGameState(state));
    connection.on("GameDealt", (state) => {
      setGameState(state);
      if (state.state === 2) stopTimer();
      else resetTimer(TURN_TIME);
    });
    connection.on("PlayerHit", onGameState);
    connection.on("PlayerStand", onGameState);
    connection.on("PlayerDoubleDown", onGameState);

    return () => {
      connection.off("YourSeat");
      connection.off("BalanceUpdate");
      connection.off("StartGame");
      connection.off("BetPlaced");
      connection.off("GameDealt");
      connection.off("PlayerHit");
      connection.off("PlayerStand");
      connection.off("PlayerDoubleDown");
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
    await connection.invoke("StartGame", roomId);
  }
  async function handlePlaceBet() {
    const max = Math.min(MAX_BET, balance || MAX_BET);
    const amount = Math.max(MIN_BET, Math.min(max, Number(betAmount) || MIN_BET));
    setBetAmount(amount);
    await connection.invoke("PlaceBet", roomId, amount);
  }
  async function handleHit() {
    await connection.invoke("BlackJackPlayerHit", roomId);
  }
  async function handleStand() {
    await connection.invoke("BlackJackPlayerStand", roomId);
  }
  async function handleDoubleDown() {
    await connection.invoke("BlackJackPlayerDoubleDown", roomId);
  }
  async function handleKick(seatIndex) {
    await connection.invoke("KickPlayer", roomId, seatIndex);
  }
  async function handleLeave() {
    await connection.invoke("LeaveRoom");
    onLeave();
  }

  function renderPlayerList(showKick) {
    if (roomPlayers.length === 0) return null;
    return (
      <ul className="player-list">
        {roomPlayers.map((p, i) => (
          <li key={i}>
            {p.nickname} {p.isHost ? "\uD83D\uDC51" : p.isReady ? "\u2713" : "\u2014"}
            {showKick && isHost && !p.isHost && (
              <button className="kick-btn" onClick={() => handleKick(p.seatIndex)}>Kick</button>
            )}
          </li>
        ))}
      </ul>
    );
  }

  function renderResult(index) {
    const r = gameState.results[index];
    if (r === null || r === undefined) return null;
    const label = r === 0 ? "Win" : r === 1 ? "Lose" : "Push";
    const cls = r === 0 ? "win" : r === 1 ? "lose" : "push";
    const w = gameState.winnings?.[index];
    return (
      <div>
        <span className={`result-badge ${cls}`}>
          {label}
          {w != null && <span className="winnings"> ({w >= 0 ? "+" : ""}{w})</span>}
        </span>
      </div>
    );
  }

  // Pre-game room lobby
  if (!gameState) {
    return (
      <div className="room-lobby">
        <h2>Room {roomId}</h2>
        <p className="sub">Players: {playerCount} / {maxPlayers}</p>
        {renderPlayerList(true)}
        <div className="btn-row">
          {isHost ? (
            <button
              className="btn btn-success"
              onClick={handleStart}
              disabled={roomPlayers.filter(p => !p.isHost).some(p => !p.isReady) && roomPlayers.length > 1}
            >
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

  const finished = gameState.state === 2;
  const betting = gameState.state === 3;

  // Betting phase
  if (betting) {
    const myBetPlaced = myIndex >= 0 && gameState.bets[myIndex] > 0;
    return (
      <div className="game-container">
        <div className="game-header">
          <span className="room-info">Room {roomId} &middot; {playerCount}/{maxPlayers}</span>
          {balance !== null && <span className="balance">Balance: {balance}</span>}
        </div>
        <div className="table">
          <div className="betting-area">
            <div className="betting-title">Place Your Bet ({timeLeft}s)</div>
            <p className="text-muted mb-16" style={{ color: 'rgba(255,255,255,0.5)' }}>
              Min: {MIN_BET} &middot; Max: {Math.min(MAX_BET, balance || MAX_BET)}
            </p>
            <div className="betting-players">
              {gameState.playerNames?.map((name, i) => (
                <div className="betting-player" key={i}>
                  <div className="name">{name}</div>
                  <div className={`status ${gameState.bets[i] > 0 ? "placed" : ""}`}>
                    {gameState.bets[i] > 0 ? `Bet: ${gameState.bets[i]}` : "Waiting..."}
                  </div>
                </div>
              ))}
            </div>
            {!myBetPlaced && myIndex >= 0 && (
              <div className="bet-input-group">
                <input
                  className="bet-input"
                  type="number"
                  min={MIN_BET}
                  max={Math.min(MAX_BET, balance || MAX_BET)}
                  value={betAmount}
                  onChange={(e) => setBetAmount(e.target.value)}
                  onBlur={() => {
                    const max = Math.min(MAX_BET, balance || MAX_BET);
                    setBetAmount(Math.max(MIN_BET, Math.min(max, Number(betAmount) || MIN_BET)));
                  }}
                />
                <button className="btn btn-success" onClick={handlePlaceBet}>Place Bet</button>
              </div>
            )}
            {myBetPlaced && (
              <p style={{ color: '#4ade80', marginTop: 12 }}>Bet placed! Waiting for others...</p>
            )}
          </div>
        </div>
      </div>
    );
  }

  // Main game view
  return (
    <div className="game-container">
      <div className="game-header">
        <span className="room-info">Room {roomId} &middot; {playerCount}/{maxPlayers}</span>
        {balance !== null && <span className="balance">Balance: {balance}</span>}
      </div>

      <div className="table">
        <div className="deck-info">
          Deck: {gameState.cardsRemaining}/{gameState.totalCards} (reshuffle at {gameState.reshuffleThreshold})
        </div>

        {/* Dealer */}
        <div className="hand-area dealer-area">
          <div className="hand-label">
            <span className="dealer-tag">Dealer</span>
            <span className="value">({gameState.dealerHand.value})</span>
          </div>
          <div className="cards">
            {gameState.dealerHand.cards.map((c, i) => (
              <Card key={i} card={c} />
            ))}
          </div>
        </div>

        <div className="table-divider" />

        {/* Other players (compact) */}
        {gameState.playerHands.length > 1 && (
          <div className="others-row">
            {gameState.playerHands.map((hand, i) => {
              if (i === myIndex) return null;
              const isActive = i === gameState.currentIndex && !finished;
              return (
                <div key={i} className={`other-player ${isActive ? "active" : ""}`}>
                  <div className="other-label">
                    <span className="name">{gameState.playerNames?.[i] || `Player ${i}`}</span>
                    <span className="value">({hand.value})</span>
                    <span className="bet">Bet: {gameState.bets[i]}</span>
                    {isActive && <span className="timer">{timeLeft}s</span>}
                  </div>
                  <MiniCards cards={hand.cards} />
                  {finished && renderResult(i)}
                </div>
              );
            })}
          </div>
        )}

        <div className="table-divider" />

        {/* My hand (large, centered) */}
        {myIndex >= 0 && gameState.playerHands[myIndex] && (() => {
          const hand = gameState.playerHands[myIndex];
          const isActive = myIndex === gameState.currentIndex && !finished;
          return (
            <div className={`hand-area my-hand ${isActive ? "active" : ""}`}>
              <div className="hand-label">
                <span className="name">{gameState.playerNames?.[myIndex] || "You"} (You)</span>
                <span className="value">({hand.value})</span>
                <span className="bet">Bet: {gameState.bets[myIndex]}</span>
                {isActive && <span className="timer">{timeLeft}s</span>}
              </div>
              <div className="cards">
                {hand.cards.map((c, j) => (
                  <Card key={j} card={c} />
                ))}
              </div>
              {finished && renderResult(myIndex)}
            </div>
          );
        })()}

        {/* Actions */}
        {!finished && gameState.currentIndex === myIndex && (
          <div className="actions">
            <button className="btn btn-success" onClick={handleHit}>Hit</button>
            <button className="btn btn-danger" onClick={handleStand}>Stand</button>
            {gameState.playerHands[myIndex]?.cards.length === 2 &&
              balance >= gameState.bets[myIndex] * 2 && (
              <button className="btn btn-warning" onClick={handleDoubleDown}>Double Down</button>
            )}
          </div>
        )}

        {/* Post-game */}
        {finished && (
          <div className="lobby-area">
            {renderPlayerList(true)}
            <div className="btn-row">
              {isHost ? (
                <button
                  className="btn btn-success"
                  onClick={handleStart}
                  disabled={roomPlayers.filter(p => !p.isHost).some(p => !p.isReady) && roomPlayers.length > 1}
                >
                  Next Round
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
    </div>
  );
}
