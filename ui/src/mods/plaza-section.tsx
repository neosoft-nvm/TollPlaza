import { trigger } from "cs2/api";
import { getModule } from "cs2/modding";
import { Button } from "cs2/ui";
import { useState } from "react";
import { count, dollars, MonthCompare, MonthPair } from "./stats";
import styles from "./toll-panel.module.scss";

// Written by PlazaInfoSection.cs.
interface Stats extends MonthPair {
    /** Vehicles charged in the last minute of simulation time (a minute at normal speed). */
    perMinute: number;
    /** Seconds of that minute measured so far (under 60 right after loading the city). */
    measuredSeconds: number;
}

interface Booth {
    cash: boolean;
    closed: boolean;
    stats: Stats;
}

interface PlazaSectionProps {
    name: string;
    outdated: boolean;
    plaza: Stats;
    booths: Booth[];
    /** True when the plaza charges its own rates instead of the city-wide ones. */
    ownRates: boolean;
    /** What the plaza charges now, in cents (its own rates, or the city's). */
    carRate: number;
    truckRate: number;
    busRate: number;
}

const RATE_STEP_CENTS = 25;
const usd = (cents: number) => `$${(cents / 100).toFixed(2)}`;

/** One of the plaza's own rates with − and + buttons. */
const PlazaRateRow = ({ label, id, cents }: { label: string; id: string; cents: number }) => (
    <div className={styles.row}>
        <span className={styles.label}>{label}</span>
        <div className={styles.stepper}>
            <Button variant="flat" className={styles.step}
                onSelect={() => trigger("tollPlaza", "adjustPlazaRate", id, -RATE_STEP_CENTS)}>−</Button>
            <span className={styles.rate}>{usd(cents)}</span>
            <Button variant="flat" className={styles.step}
                onSelect={() => trigger("tollPlaza", "adjustPlazaRate", id, RATE_STEP_CENTS)}>+</Button>
        </div>
    </div>
);

/** "12 vehicles/min", or "3 in the first 20 s" while the first minute after loading is still running. */
const rate = (s: Stats) =>
    s.measuredSeconds >= 60 ? `${s.perMinute} vehicles/min` : `${s.perMinute} in the first ${s.measuredSeconds} s`;

const RateRow = ({ stats }: { stats: Stats }) => (
    <div className={styles.row}>
        <span className={styles.label}>Measured, last minute</span>
        <span className={styles.value}>{rate(stats)}</span>
    </div>
);

/**
 * The plaza's name with Rename and Auto name buttons. A plaza keeps its name (even when a district is drawn
 * later); "Auto name" picks a new one from the district, else the city. Saving an empty name does the same.
 */
const PlazaName = ({ name }: { name: string }) => {
    const [editing, setEditing] = useState(false);
    const [text, setText] = useState(name);
    const save = (value: string) => {
        trigger("tollPlaza", "renamePlaza", value);
        setEditing(false);
    };

    if (!editing) {
        return (
            <div className={styles.row}>
                <span className={styles.plazaName}>{name}</span>
                <Button variant="flat" className={styles.switch} onSelect={() => { setText(name); setEditing(true); }}>
                    Rename
                </Button>
                <Button variant="flat" className={styles.switch} onSelect={() => trigger("tollPlaza", "autoNamePlaza")}>
                    Auto name
                </Button>
            </div>
        );
    }
    return (
        <div className={styles.row}>
            <input className={styles.nameInput} value={text} maxLength={40} autoFocus
                onChange={e => setText(e.target.value)}
                onKeyDown={e => {
                    if (e.key === "Enter") save(text);
                    if (e.key === "Escape") setEditing(false);
                }} />
            <Button variant="flat" className={styles.switch} onSelect={() => save(text)}>Save</Button>
            <Button variant="flat" className={styles.switch} onSelect={() => setEditing(false)}>Cancel</Button>
        </div>
    );
};

// The game's own info-panel section frame, so the plaza section looks like the others.
const InfoSection: any =
    getModule("game-ui/game/components/selected-info-panel/shared-components/info-section/info-section.tsx", "InfoSection")
    ?? ((p: any) => <div>{p.children}</div>);

/** "Toll Plaza" section of the info panel when a plaza is clicked: plaza totals and each booth. */
const PlazaSection = (props: PlazaSectionProps) => {
    const [booth, setBooth] = useState<number | null>(null);
    // The rate editor and a booth's details share one spot, so the panel never grows by both (a tall panel crashed the UI once).
    const [editRates, setEditRates] = useState(false);
    const selected = booth !== null && booth < props.booths.length ? props.booths[booth] : null;

    return (
        <InfoSection disableFocus={true}>
            <div className={styles.section}>
                <PlazaName name={props.name} />
                {props.outdated && (
                    <div className={styles.warning}>
                        Old road layout: bulldoze and re-place this plaza for the current lanes and booth counts.
                    </div>
                )}
            </div>
            <div className={styles.section}>
                <div className={styles.heading}>Whole plaza</div>
                <RateRow stats={props.plaza} />
                <div className={styles.row}>
                    <span className={styles.label}>{`Tolls (${props.ownRates ? "this plaza" : "city"}): ${usd(props.carRate)} / ${usd(props.truckRate)} / ${usd(props.busRate)}`}</span>
                    <Button variant="flat" className={styles.switch}
                        onSelect={() => { setBooth(null); setEditRates(!editRates); }}>
                        {editRates ? "Done" : "Rates"}
                    </Button>
                </div>
                <MonthCompare stats={props.plaza} />
            </div>
            <div className={styles.section}>
                <div className={styles.heading}>Booths (click one for its numbers)</div>
                <div className={styles.booths}>
                    {props.booths.map((b, i) => (
                        <Button key={i} variant="flat"
                            className={`${styles.booth} ${b.closed ? styles.closed : b.cash ? styles.cash : styles.etc} ${booth === i ? styles.boothSelected : ""}`}
                            onSelect={() => { setEditRates(false); setBooth(booth === i ? null : i); }}>
                            {/* Stacked in a div of its own: the game's Button lays its children out in a row. */}
                            <div className={styles.boothStack}>
                                <div>{`${i + 1}`}</div>
                                <div className={styles.boothType}>{b.closed ? "Closed" : b.cash ? "Cash" : "E-Toll"}</div>
                                <div className={styles.boothMoney}>{dollars(b.stats.thisMonth.cents)}</div>
                                <div className={styles.boothType}>{`${b.stats.perMinute}/min`}</div>
                            </div>
                        </Button>
                    ))}
                </div>
            </div>
            {editRates && (
                <div className={styles.section}>
                    <div className={styles.row}>
                        <span className={styles.heading}>{props.ownRates ? "This plaza's own rates" : "Using the city's rates"}</span>
                        <Button variant="flat" className={styles.switch}
                            onSelect={() => trigger("tollPlaza", "togglePlazaRates")}>
                            {props.ownRates ? "Use city rates" : "Set own rates"}
                        </Button>
                    </div>
                    {props.ownRates ? (
                        <>
                            <PlazaRateRow label="Cars" id="car" cents={props.carRate} />
                            <PlazaRateRow label="Trucks" id="truck" cents={props.truckRate} />
                            <PlazaRateRow label="Buses" id="bus" cents={props.busRate} />
                        </>
                    ) : (
                        <div className={styles.note}>Change the city-wide rates in the Toll Plaza panel, or set this plaza's own.</div>
                    )}
                </div>
            )}
            {selected && booth !== null && (
                <div className={styles.section}>
                    <div className={styles.row}>
                        <span className={styles.heading}>{`Booth ${booth + 1} — ${selected.cash ? "Cash" : "E-Toll"}${selected.closed ? " (closed)" : ""}`}</span>
                        <Button variant="flat" className={styles.switch}
                            onSelect={() => trigger("tollPlaza", "toggleBooth", booth)}>
                            {`Switch to ${selected.cash ? "E-Toll" : "Cash"}`}
                        </Button>
                        <Button variant="flat" className={styles.switch}
                            onSelect={() => trigger("tollPlaza", "toggleBoothClosed", booth)}>
                            {selected.closed ? "Open lane" : "Close lane"}
                        </Button>
                    </div>
                    <RateRow stats={selected.stats} />
                    <MonthCompare stats={selected.stats} />
                    <div className={styles.note}>
                        {`${count(selected.stats.thisMonth.cars + selected.stats.thisMonth.trucks + selected.stats.thisMonth.buses)} paying vehicles this month`}
                    </div>
                </div>
            )}
        </InfoSection>
    );
};

/** Adds the section to the game's info panel (keyed by the C# class name). */
export const registerPlazaSection = (components: any) => {
    components["TollPlaza.PlazaInfoSection"] = (props: PlazaSectionProps) => <PlazaSection {...props} />;
    return components;
};
