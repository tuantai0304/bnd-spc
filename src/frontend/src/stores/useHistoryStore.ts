import { create } from 'zustand';
import { ApiError } from '../api/client';
import { getTravelHistory } from '../api/endpoints';
import type { HistoryPage } from '../api/types';

interface HistoryStore {
  page: number;
  pageSize: number;
  data: HistoryPage | null;
  loading: boolean;
  error: ApiError | null;
  /** Set the paging window (from the URL) and fetch it. */
  load: (page: number, pageSize: number) => Promise<void>;
  /** Refetch the current window — the Refresh button. */
  refresh: () => Promise<void>;
  /**
   * History is a review screen, not a live one: rows only appear when a trip
   * becomes terminal. There is no interval, so these are deliberately inert
   * and exist to keep the store shape uniform across the app.
   */
  startPolling: () => void;
  stopPolling: () => void;
}

export const useHistoryStore = create<HistoryStore>((set, get) => ({
  page: 1,
  pageSize: 25,
  data: null,
  loading: false,
  error: null,

  async load(page, pageSize) {
    set({ page, pageSize });
    await get().refresh();
  },

  async refresh() {
    const { page, pageSize } = get();
    set({ loading: get().data === null, error: null });
    try {
      const data = await getTravelHistory(page, pageSize);
      // A slower earlier request must not overwrite a newer window's rows.
      if (get().page !== page || get().pageSize !== pageSize) return;
      set({ data, loading: false });
    } catch (e) {
      set({ error: e as ApiError, loading: false });
    }
  },

  startPolling() {},
  stopPolling() {},
}));
