import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { getValidToken } from "./api";

const BACKEND_URL = import.meta.env.VITE_BACKEND_URL
  ? `${import.meta.env.VITE_BACKEND_URL}/hub/blackjack`
  : (import.meta.env.PROD ? '/hub/blackjack' : `http://${window.location.hostname}:5087/hub/blackjack`);

export function createConnection() {
  return new HubConnectionBuilder()
    .withUrl(BACKEND_URL, {
      accessTokenFactory: () => getValidToken(),
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
}
