import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

const BACKEND_URL = import.meta.env.VITE_BACKEND_URL
  ? `${import.meta.env.VITE_BACKEND_URL}/hub/avalon`
  : (import.meta.env.PROD ? '/hub/avalon' : `http://${window.location.hostname}:5087/hub/avalon`);

export function createAvalonConnection(token) {
  return new HubConnectionBuilder()
    .withUrl(BACKEND_URL, {
      accessTokenFactory: () => token,
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
}
