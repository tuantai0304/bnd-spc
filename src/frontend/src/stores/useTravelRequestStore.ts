import { create } from 'zustand';
import { ApiError } from '../api/client';
import { getTravelRequest } from '../api/endpoints';
import { createPoller } from '../lib/polling';
import { isTerminal, type TravelRequest } from '../api/types';

const POLL_MS = 1000;

interface TravelRequestStore {
  id: number | null;
  data: TravelRequest | null;
  loading: boolean;
  error: ApiError | null;
  /** Point the store at a call. Clears any previous one's data first. */
  load: (id: number) => void;
  refresh: () => Promise<void>;
  startPolling: () => void;
  stopPolling: () => void;
}

export const useTravelRequestStore = create<TravelRequestStore>((set, get) => ({
  id: null,
  data: null,
  loading: false,
  error: null,

  load(id) {
    if (get().id === id) return;
    poller.stop();
    set({ id, data: null, loading: false, error: null });
  },

  async refresh() {
    const { id } = get();
    if (id === null) return;
    set({ loading: get().data === null });
    try {
      const data = await getTravelRequest(id);
      // Guard against a response for a call we have since navigated away from.
      if (get().id !== id) return;
      set({ data, loading: false, error: null });

      // Completed and Rejected are terminal — no further request can change
      // anything, so stop rather than poll a frozen record forever.
      if (isTerminal(data.status)) poller.stop();
    } catch (e) {
      if (get().id !== id) return;
      set({ error: e as ApiError, loading: false });
      // A 404 will never become a 200; polling it is pure noise.
      if ((e as ApiError).status === 404) poller.stop();
    }
  },

  startPolling() {
    poller.start();
  },
  stopPolling() {
    poller.stop();
  },
}));

const poller = createPoller(() => useTravelRequestStore.getState().refresh(), POLL_MS);
