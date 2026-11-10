/** Tells the other tabs of this browser that the session changed. */
export type SessionMessage = { type: "login" } | { type: "logout" };

const CHANNEL_NAME = "digital-wallet-session";

export interface SessionChannel {
  post(message: SessionMessage): void;
  close(): void;
}

function isSessionMessage(data: unknown): data is SessionMessage {
  if (typeof data !== "object" || data === null || !("type" in data)) {
    return false;
  }

  return data.type === "login" || data.type === "logout";
}

/** Returns null where BroadcastChannel is not available (for example, during server rendering). */
export function openSessionChannel(onMessage: (message: SessionMessage) => void): SessionChannel | null {
  if (typeof BroadcastChannel === "undefined") {
    return null;
  }

  const channel = new BroadcastChannel(CHANNEL_NAME);
  channel.onmessage = (event: MessageEvent<unknown>) => {
    if (isSessionMessage(event.data)) {
      onMessage(event.data);
    }
  };

  return {
    post: (message) => channel.postMessage(message),
    close: () => channel.close(),
  };
}
