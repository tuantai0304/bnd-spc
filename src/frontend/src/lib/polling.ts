/**
 * A start/stop interval that cannot be started twice.
 *
 * Every store owns one of these; pages start it on mount and stop it on
 * unmount, so no interval outlives the page that needs it. Polling is not a
 * design preference here — the simulation advances shuttles every 500 ms and
 * the API exposes no WebSocket, SSE, or SignalR endpoint.
 */
export function createPoller(fn: () => void | Promise<void>, ms: number) {
  let id: ReturnType<typeof setInterval> | null = null;
  return {
    start() {
      if (id === null) {
        void fn(); // fire immediately, then on the interval
        id = setInterval(() => void fn(), ms);
      }
    },
    stop() {
      if (id !== null) {
        clearInterval(id);
        id = null;
      }
    },
    get running() {
      return id !== null;
    },
  };
}
