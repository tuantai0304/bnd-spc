interface PagerProps {
  page: number;
  pageSize: number;
  totalCount: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
}

export const PAGE_SIZES = [10, 25, 50, 100] as const;

/**
 * The envelope carries only page/pageSize/totalCount/items — no totalPages, no
 * hasNext, no hasPrevious. All three are derived here.
 */
export function Pager({
  page,
  pageSize,
  totalCount,
  onPageChange,
  onPageSizeChange,
}: PagerProps) {
  // Math.max(1, …) matters: with totalCount 0 the naive formula yields 0, and
  // "Page 1 of 0" looks like a bug.
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const hasPrevious = page > 1;
  const hasNext = page < totalPages;

  const from = (page - 1) * pageSize + 1;
  const to = Math.min(page * pageSize, totalCount);

  const button =
    'rounded-md border border-edge px-3 py-1.5 text-sm hover:bg-raised disabled:cursor-not-allowed disabled:opacity-40 disabled:hover:bg-transparent';

  return (
    <div className="flex flex-wrap items-center justify-between gap-4 text-sm">
      <p className="text-muted tnum">
        {totalCount === 0 ? 'No records' : `Showing ${from}–${to} of ${totalCount}`}
      </p>

      <label className="flex items-center gap-2 text-muted">
        <span>Per page</span>
        <select
          value={pageSize}
          onChange={(e) => onPageSizeChange(Number(e.target.value))}
          className="rounded-md border border-edge bg-panel px-2 py-1 text-ink"
        >
          {PAGE_SIZES.map((n) => (
            <option key={n} value={n}>
              {n}
            </option>
          ))}
        </select>
      </label>

      <div className="flex items-center gap-3">
        <button
          type="button"
          className={button}
          disabled={!hasPrevious}
          onClick={() => onPageChange(page - 1)}
        >
          ‹ Prev
        </button>
        <span className="tnum text-muted">
          Page {page} of {totalPages}
        </span>
        <button
          type="button"
          className={button}
          disabled={!hasNext}
          onClick={() => onPageChange(page + 1)}
        >
          Next ›
        </button>
      </div>
    </div>
  );
}
