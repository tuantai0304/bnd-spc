import { create } from 'zustand';
import { ApiError } from '../api/client';
import { getShuttles } from '../api/endpoints';
import { createPoller } from '../lib/polling';
import type { Shuttle } from '../api/types';

const POLL_MS = 1000;

interface FleetStore {
  data: Shuttle[] | null;
  loading: boolean;
  error: ApiError | null;
  /** When the last *successful* fetch landed — drives the "last updated" line. */
  updatedAt: Date | null;
  refresh: () => Promise<void>;
  startPolling: () => void;
  stopPolling: () => void;
}

export const useFleetStore = create<FleetStore>((set, get) => ({
  data: null,
  loading: false,
  error: null,
  updatedAt: null,

  async refresh() {
    // `loading` is true only on the first load. A spinner every second would
    // make the dashboard unreadable; polls keep the stale data on screen.
    set({ loading: get().data === null });
    try {
      set({ data: await getShuttles(), loading: false, error: null, updatedAt: new Date() });
    } catch (e) {
      // Keep `data` — a failed poll must never blank a dashboard that loaded.
      set({ error: e as ApiError, loading: false });
    }
  },

  startPolling() {
    poller.start();
  },
  stopPolling() {
    poller.stop();
  },
}));

// The fleet never settles on its own: a call may be placed from anywhere, so
// an idle fleet can become busy with no action on this page.
const poller = createPoller(() => useFleetStore.getState().refresh(), POLL_MS);
