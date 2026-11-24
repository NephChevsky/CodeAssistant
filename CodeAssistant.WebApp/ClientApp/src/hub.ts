import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';

export const connection = new HubConnectionBuilder()
  .withUrl('/hubs/assistant')
  .withAutomaticReconnect()
  .configureLogging(LogLevel.Warning)
  .build();

let starting: Promise<void> | null = null;

/** Starts the shared hub connection once; safe to call repeatedly. */
export function ensureStarted(): Promise<void> {
  if (connection.state === HubConnectionState.Connected) {
    return Promise.resolve();
  }

  starting ??= connection.start().finally(() => {
    starting = null;
  });
  return starting;
}

/** Hub errors are prefixed with the invocation details; keep only the message. */
export function errorMessage(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  return message.replace(/^An unexpected error occurred invoking '[^']+' on the server\.\s*(HubException:\s*)?/, '');
}
