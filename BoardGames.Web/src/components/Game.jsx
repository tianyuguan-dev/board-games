import { useState, useEffect } from "react";

export default function Game({ connection, roomId, onLeave }) {
  const [gameState, setGameState] = useState(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    connection.on("StartGame", (state) => setGameState(state));
    connection.on("PlayerHit", (state) => setGameState(state));
    connection.on("PlayerStand", (state) => setGameState(state));
    connection.on("PlayerDisconnected", () => console.log("A player disconnected"));
    connection.on("PlayerReady", () => console.log("A player is ready"));
    connection.on("PlayerUnReady", () => console.log("A player is not ready"));

    return () => {
      connection.off("StartGame");
      connection.off("PlayerHit");
      connection.off("PlayerStand");
      connection.off("PlayerDisconnected");
      connection.off("PlayerReady");
      connection.off("PlayerUnReady");
    };
  }, [connection]);

  async function handleReady() {
    await connection.invoke("Ready", roomId);
    setReady(true);
  }

  async function handleUnready() {
    await connection.invoke("Unready", roomId);
    setReady(false);
  }

  async function handleStartGame() {
    try {
      await connection.invoke("StartGame", roomId);
      setReady(false);
    } catch (err) {
      console.error("Start failed:", err);
    }
  }

  async function handleHit() {
    await connection.invoke("BlackJackPlayerHit", roomId);
  }

  async function handleStand() {
    await connection.invoke("BlackJackPlayerStand", roomId);
  }

  async function handleLeave() {
    await connection.invoke("LeaveRoom");
    onLeave();
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
        <p>Waiting for players...</p>
        {ready ? (
          <button onClick={handleUnready}>Cancel Ready</button>
        ) : (
          <button onClick={handleReady}>Ready</button>
        )}
        <button onClick={handleStartGame}>Start Game</button>
        <br />
        <button onClick={handleLeave}>Leave Room</button>
      </div>
    );
  }

  const finished = gameState.state === 2;

  return (
    <div>
      <h2>Room: {roomId}</h2>

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
            Player {i} {i === gameState.currentIndex && !finished ? "⬅ your turn" : ""}
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

      {!finished && (
        <div>
          <button onClick={handleHit}>Hit</button>
          <button onClick={handleStand}>Stand</button>
        </div>
      )}

      {finished && (
        <div>
          {ready ? (
            <button onClick={handleUnready}>Cancel Ready</button>
          ) : (
            <button onClick={handleReady}>Ready (Next Round)</button>
          )}
          <button onClick={handleStartGame}>Start Game</button>
          <button onClick={handleLeave}>Leave Room</button>
        </div>
      )}
    </div>
  );
}
