import { trigger } from "cs2/api";
import { getModule } from "cs2/modding";
import { Button } from "cs2/ui";
import { useState } from "react";
import { count, dollars, MonthCompare, MonthPair } from "./stats";
import styles from "./toll-panel.module.scss";

// Written by PlazaInfoSection.cs.
interface Booth {
    cash: boolean;
    stats: MonthPair;
}

interface PlazaSectionProps {
    name: string;
    custom: boolean;
    outdated: boolean;
    plaza: MonthPair;
    booths: Booth[];
}

/** The plaza's name with a Rename button; saving an empty name goes back to the automatic one. */
const PlazaName = ({ name, custom }: { name: string; custom: boolean }) => {
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
            {custom && <Button variant="flat" className={styles.switch} onSelect={() => save("")}>Auto</Button>}
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
    const selected = booth !== null && booth < props.booths.length ? props.booths[booth] : null;

    return (
        <InfoSection disableFocus={true}>
            <div className={styles.section}>
                <PlazaName name={props.name} custom={props.custom} />
                {props.outdated && (
                    <div className={styles.warning}>
                        Old road layout: bulldoze and re-place this plaza for the current lanes and booth counts.
                    </div>
                )}
            </div>
            <div className={styles.section}>
                <div className={styles.heading}>Whole plaza</div>
                <MonthCompare stats={props.plaza} />
            </div>
            <div className={styles.section}>
                <div className={styles.heading}>Booths (click one for its numbers)</div>
                <div className={styles.booths}>
                    {props.booths.map((b, i) => (
                        <Button key={i} variant="flat"
                            className={`${styles.booth} ${b.cash ? styles.cash : styles.etc} ${booth === i ? styles.boothSelected : ""}`}
                            onSelect={() => setBooth(booth === i ? null : i)}>
                            <div>{i + 1}</div>
                            <div className={styles.boothType}>{b.cash ? "Cash" : "E-Toll"}</div>
                            <div className={styles.boothMoney}>{dollars(b.stats.thisMonth.cents)}</div>
                        </Button>
                    ))}
                </div>
            </div>
            {selected && booth !== null && (
                <div className={styles.section}>
                    <div className={styles.row}>
                        <span className={styles.heading}>{`Booth ${booth + 1} — ${selected.cash ? "Cash" : "E-Toll"}`}</span>
                        <Button variant="flat" className={styles.switch}
                            onSelect={() => trigger("tollPlaza", "toggleBooth", booth)}>
                            Switch to {selected.cash ? "E-Toll" : "Cash"}
                        </Button>
                    </div>
                    <MonthCompare stats={selected.stats} />
                    <div className={styles.note}>
                        {count(selected.stats.thisMonth.cars + selected.stats.thisMonth.trucks + selected.stats.thisMonth.buses)} paying vehicles this month
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
