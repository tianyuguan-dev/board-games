import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { getValidToken } from "./api";

const BACKEND_URL = import.meta.env.VITE_BACKEND_URL
  ? `${import.meta.env.VITE_BACKEND_URL}/hub/blackjack`
  : (import.meta.env.PROD ? '/hub/blackjack' : `http://${window.location.hostname}:5087/hub/blackjack`);

const RECONNECT_DELAYS = [0, 1000, 2000, 5000, 5000, 10000, 10000, 30000, 30000, 60000];

export function createConnection() {
  return new HubConnectionBuilder()
    .withUrl(BACKEND_URL, {
      accessTokenFactory: () => getValidToken(),
    })
    .withAutomaticReconnect(RECONNECT_DELAYS)
    .configureLogging(LogLevel.Information)
    .build();
}
