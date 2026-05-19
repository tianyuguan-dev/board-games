import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { getValidToken } from "./api";

const BACKEND_URL = import.meta.env.VITE_BACKEND_URL
  ? `${import.meta.env.VITE_BACKEND_URL}/hub/avalon`
  : (import.meta.env.PROD ? '/hub/avalon' : `http://${window.location.hostname}:5087/hub/avalon`);

export function createAvalonConnection() {
  return new HubConnectionBuilder()
    .withUrl(BACKEND_URL, {
      accessTokenFactory: () => getValidToken(),
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
}
