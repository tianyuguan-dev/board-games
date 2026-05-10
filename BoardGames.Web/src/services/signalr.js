import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

const BACKEND_URL = "http://localhost:5087/hub/blackjack";

export function createConnection(token) {
  return new HubConnectionBuilder()
    .withUrl(BACKEND_URL, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
}
