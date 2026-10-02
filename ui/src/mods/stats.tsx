import styles from "./toll-panel.module.scss";

// Shapes written by PlazaInfoSection.WriteMonth / TollUISystem (C#).
export interface Month {
    cars: number;
    trucks: number;
    buses: number;
    exempt: number;
    cents: number;
}

export interface MonthPair {
    thisMonth: Month;
    lastMonth: Month;
}

// Built by hand: the game's UI (Gameface) ignores toLocaleString's options, which showed "$2587.5".
const grouped = (n: number) => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ",");

export const dollars = (cents: number) => {
    const c = Math.round(Math.abs(cents));
    return `${cents < 0 ? "-" : ""}$${grouped(Math.floor(c / 100))}.${String(c % 100).padStart(2, "0")}`;
};
export const count = (n: number) => grouped(Math.round(n));

/** Change from last month: "+12%" in green, "-5%" in red, "new" when last month had nothing. */
const Change = ({ now, before }: { now: number; before: number }) => {
    if (before === 0) {
        return <span className={styles.change}>{now > 0 ? "new" : "–"}</span>;
    }
    const pct = Math.round(((now - before) / before) * 100);
    const cls = pct > 0 ? styles.up : pct < 0 ? styles.down : styles.change;
    return <span className={`${styles.change} ${cls}`}>{pct > 0 ? "+" : ""}{pct}%</span>;
};

const CompareRow = ({ label, now, before, format, allTime }:
    { label: string; now: number; before: number; format: (n: number) => string; allTime?: number }) => (
    <div className={styles.compareRow}>
        <span className={styles.label}>{label}</span>
        <span className={styles.num}>{format(now)}</span>
        <span className={styles.num}>{format(before)}</span>
        <Change now={now} before={before} />
        {allTime !== undefined && <span className={styles.num}>{format(allTime)}</span>}
    </div>
);

/** All-time totals for the extra column in the control panel (same rows as a month). */
export type AllTime = Month;

/**
 * This month vs last month: vehicles by type and revenue, with the change in percent; with allTime, a fourth
 * "All time" column too (the control panel's city totals).
 */
export const MonthCompare = ({ stats, allTime }: { stats: MonthPair; allTime?: AllTime }) => {
    const a = stats.thisMonth, b = stats.lastMonth;
    const total = (m: Month) => m.cars + m.trucks + m.buses + m.exempt;
    return (
        <div>
            <div className={`${styles.compareRow} ${styles.compareHead}`}>
                <span className={styles.label}></span>
                <span className={styles.num}>This month</span>
                <span className={styles.num}>Last month</span>
                <span className={styles.change}>Change</span>
                {allTime && <span className={styles.num}>All time</span>}
            </div>
            <CompareRow label="Cars" now={a.cars} before={b.cars} format={count} allTime={allTime?.cars} />
            <CompareRow label="Trucks" now={a.trucks} before={b.trucks} format={count} allTime={allTime?.trucks} />
            <CompareRow label="Buses" now={a.buses} before={b.buses} format={count} allTime={allTime?.buses} />
            <CompareRow label="Exempt" now={a.exempt} before={b.exempt} format={count} allTime={allTime?.exempt} />
            <CompareRow label="All vehicles" now={total(a)} before={total(b)} format={count}
                allTime={allTime && total(allTime)} />
            <CompareRow label="Revenue" now={a.cents} before={b.cents} format={dollars} allTime={allTime?.cents} />
        </div>
    );
};
