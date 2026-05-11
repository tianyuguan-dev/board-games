import { useState, useEffect, useRef } from "react";

const TURN_TIME = 10;

export default function Game({ connection, roomId, maxPlayers, playerCount, roomPlayers, isHost, onLeave }) {
  const [gameState, setGameState] = useState(null);
  const [ready, setReady] = useState(false);
  const [myIndex, setMyIndex] = useState(-1);
  const [timeLeft, setTimeLeft] = useState(TURN_TIME);
  const timerRef = useRef(null);

  function resetTimer() {
    clearInterval(timerRef.current);
    setTimeLeft(TURN_TIME);
    timerRef.current = setInterval(() => {
      setTimeLeft((t) => (t > 0 ? t - 1 : 0));
    }, 1000);
  }

  function stopTimer() {
    clearInterval(timerRef.current);
    timerRef.current = null;
  }

  useEffect(() => {
    return () => clearInterval(timerRef.current);
  }, []);

  useEffect(() => {
    function onGameState(state) {
      setGameState(state);
      if (state.state === 2) stopTimer();
      else resetTimer();
    }

    connection.on("YourSeat", (index) => setMyIndex(index));
    connection.on("StartGame", (state) => {
      setGameState(state);
      setReady(false);
      if (state.state === 2) stopTimer();
      else resetTimer();
    });
    connection.on("PlayerHit", onGameState);
    connection.on("PlayerStand", onGameState);

    return () => {
      connection.off("YourSeat");
      connection.off("StartGame");
      connection.off("PlayerHit");
      connection.off("PlayerStand");
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

  async function handleHit() {
    await connection.invoke("BlackJackPlayerHit", roomId);
  }

  async function handleStand() {
    await connection.invoke("BlackJackPlayerStand", roomId);
  }

  async function handleKick(seatIndex) {
    await connection.invoke("KickPlayer", roomId, seatIndex);
  }

  async function handleLeave() {
    await connection.invoke("LeaveRoom");
    onLeave();
  }

  function renderReadyOrStart(nextRound) {
    if (isHost) {
      const others = roomPlayers.filter(p => !p.isHost);
      const allReady = others.length === 0 || others.every(p => p.isReady);
      return <button onClick={handleStart} disabled={!allReady}>{nextRound ? "Start Next Round" : "Start Game"}</button>;
    }
    return ready ? (
      <button onClick={handleUnready}>Cancel Ready</button>
    ) : (
      <button onClick={handleReady}>{nextRound ? "Ready (Next Round)" : "Ready"}</button>
    );
  }

  function renderPlayerList(showKick) {
    if (roomPlayers.length === 0) return null;
    return (
      <ul>
        {roomPlayers.map((p, i) => (
          <li key={i}>
            {p.nickname} {p.isHost ? "👑" : p.isReady ? "✓ Ready" : "— Not Ready"}
            {showKick && isHost && !p.isHost && (
              <button onClick={() => handleKick(p.seatIndex)} style={{ marginLeft: 8 }}>Kick</button>
            )}
          </li>
        ))}
      </ul>
    );
  }

  function renderCard(card) {
    const suits = { 1: "♥", 2: "♠", 3: "♣", 4: "♦" };
    const ranks = {
      1: "A", 2: "2", 3: "3", 4: "4", 5: "5", 6: "6", 7: "7",
      8: "8", 9: "9", 10: "10", 11: "J", 12: "Q", 13: "K",
    };
    return `${ranks[card.rank]}${suits[card.suit]}`;
  }

  if (!gameState) {
    return (
      <div>
        <h2>Room: {roomId}</h2>
        <p>Players: {playerCount} / {maxPlayers}</p>

        {renderPlayerList(true)}

        {renderReadyOrStart(false)}
        <br />
        <button onClick={handleLeave}>Leave Room</button>
      </div>
    );
  }

  const finished = gameState.state === 2;

  return (
    <div>
      <h2>Room: {roomId}</h2>
      <p>Players: {playerCount} / {maxPlayers}</p>
      <p>Deck: {gameState.cardsRemaining} / {gameState.totalCards} remaining (reshuffle at {gameState.reshuffleThreshold})</p>

      <div>
        <h3>Dealer</h3>
        <p>
          {gameState.dealerHand.cards.map((c, i) => (
            <span key={i}>{renderCard(c)} </span>
          ))}
          ({gameState.dealerHand.value})
        </p>
      </div>

      {gameState.playerHands.map((hand, i) => (
        <div key={i}>
          <h3>
            {gameState.playerNames?.[i] || `Player ${i}`} {i === gameState.currentIndex && !finished ? `⬅ (${timeLeft}s)` : ""}
          </h3>
          <p>
            {hand.cards.map((c, j) => (
              <span key={j}>{renderCard(c)} </span>
            ))}
            ({hand.value})
          </p>
          {finished && gameState.results[i] !== null && (
            <p>
              {gameState.results[i] === 0
                ? "Win!"
                : gameState.results[i] === 1
                ? "Lose"
                : "Push"}
            </p>
          )}
        </div>
      ))}

      {!finished && gameState.currentIndex === myIndex && (
        <div>
          <button onClick={handleHit}>Hit</button>
          <button onClick={handleStand}>Stand</button>
        </div>
      )}

      {finished && (
        <div>
          {renderPlayerList(true)}
          {renderReadyOrStart(true)}
          <button onClick={handleLeave}>Leave Room</button>
        </div>
      )}
    </div>
  );
}
