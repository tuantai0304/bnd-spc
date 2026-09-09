import { create } from 'zustand';
import { ApiError } from '../api/client';
import { getPlanets } from '../api/endpoints';
import type { Planet } from '../api/types';

interface PlanetsStore {
  data: Planet[] | null;
  loading: boolean;
  error: ApiError | null;
  refresh: () => Promise<void>;
  /**
   * Planets are immutable seed data, so "polling" here means "fetch once and
   * cache forever". Keeping the name uniform with the other stores lets every
   * page use the same mount/unmount pattern.
   */
  startPolling: () => void;
  stopPolling: () => void;
}

export const usePlanetsStore = create<PlanetsStore>((set, get) => ({
  data: null,
  loading: false,
  error: null,

  async refresh() {
    set({ loading: get().data === null, error: null });
    try {
      set({ data: await getPlanets(), loading: false });
    } catch (e) {
      set({ error: e as ApiError, loading: false });
    }
  },

  startPolling() {
    // Already have them, or a fetch is in flight — nothing to do.
    if (get().data !== null || get().loading) return;
    void get().refresh();
  },

  stopPolling() {
    // No interval to cancel; planets are fetched exactly once.
  },
}));
