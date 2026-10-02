import { bindValue, trigger, useValue } from "cs2/api";
import { camera, Entity, selectedInfo } from "cs2/bindings";
import { Button, FloatingButton, Panel } from "cs2/ui";
import { useState } from "react";
import { count as countOf, dollars as money, MonthCompare, MonthPair, Month } from "./stats";
import styles from "./toll-panel.module.scss";

// Values published by TollUISystem.cs (binding group "tollPlaza").
const group = "tollPlaza";
const plazas$ = bindValue<number>(group, "plazas", 0);
const boothLanes$ = bindValue<number>(group, "boothLanes", 0);
const revenue$ = bindValue<string>(group, "revenue", "$0.00");
const revenueCents$ = bindValue<number>(group, "revenueCents", 0);
const cars$ = bindValue<number>(group, "cars", 0);
const trucks$ = bindValue<number>(group, "trucks", 0);
const buses$ = bindValue<number>(group, "buses", 0);
const exempt$ = bindValue<number>(group, "exempt", 0);
const carRate$ = bindValue<number>(group, "carRate", 250);
const truckRate$ = bindValue<number>(group, "truckRate", 600);
const busRate$ = bindValue<number>(group, "busRate", 400);
const cashMph$ = bindValue<number>(group, "cashMph", 10);
const etcMph$ = bindValue<number>(group, "etcMph", 35);
const emptyMonth: Month = { cars: 0, trucks: 0, buses: 0, exempt: 0, cents: 0 };
const months$ = bindValue<MonthPair>(group, "months", { thisMonth: emptyMonth, lastMonth: emptyMonth });

interface PlazaItem extends MonthPair {
    entity: Entity;
    name: string;
    lanes: number;
    outdated: boolean;
}
const plazaList$ = bindValue<PlazaItem[]>(group, "plazaList", []);

/** Selects the plaza (opening its info panel) and moves the camera to it. */
const goTo = (entity: Entity) => {
    selectedInfo.selectEntity(entity);
    camera.focusEntity(entity);
};

const RATE_STEP_CENTS = 25;
const ICON = "coui://tollplaza/TollPlaza.svg";

const dollars = (cents: number) => `$${(cents / 100).toFixed(2)}`;
const count = (n: number) => n.toLocaleString();

const Row = ({ label, value }: { label: string; value: string }) => (
    <div className={styles.row}>
        <span className={styles.label}>{label}</span>
        <span className={styles.value}>{value}</span>
    </div>
);

const RateRow = ({ label, id, cents }: { label: string; id: string; cents: number }) => (
    <div className={styles.row}>
        <span className={styles.label}>{label}</span>
        <div className={styles.stepper}>
            <Button variant="flat" className={styles.step}
                onSelect={() => trigger(group, "adjustRate", id, -RATE_STEP_CENTS)}>−</Button>
            <span className={styles.rate}>{dollars(cents)}</span>
            <Button variant="flat" className={styles.step}
                onSelect={() => trigger(group, "adjustRate", id, RATE_STEP_CENTS)}>+</Button>
        </div>
    </div>
);

const SPEED_STEP_MPH = 5;

const SpeedRow = ({ label, id, mph }: { label: string; id: string; mph: number }) => (
    <div className={styles.row}>
        <span className={styles.label}>{label}</span>
        <div className={styles.stepper}>
            <Button variant="flat" className={styles.step}
                onSelect={() => trigger(group, "adjustSpeed", id, -SPEED_STEP_MPH)}>−</Button>
            <span className={styles.rate}>{`${mph} mph`}</span>
            <Button variant="flat" className={styles.step}
                onSelect={() => trigger(group, "adjustSpeed", id, SPEED_STEP_MPH)}>+</Button>
        </div>
    </div>
);

// Where the panel was last dragged to, as the game's draggable panel stores it: the fraction of the screen
// space left beside/below the panel (0 = left/top edge, 1 = right/bottom). The game gives no callback when
// it moves, so it's read back from the panel on close; the panel then reopens where it was left.
let lastPosition = { x: 0.03, y: 0.08 };

const rememberPosition = () => {
    const panel = document.querySelector(`.${styles.panel}`) as HTMLElement | null;
    if (!panel) {
        return;
    }
    const r = panel.getBoundingClientRect();
    const freeX = window.innerWidth - r.width;
    const freeY = window.innerHeight - r.height;
    const fraction = (at: number, free: number) => Math.min(Math.max(at / free, 0), 1);
    if (freeX > 0 && freeY > 0) {
        lastPosition = { x: fraction(r.left, freeX), y: fraction(r.top, freeY) };
    }
};

/** Top-left toolbar button that opens the Toll Plaza control panel (drag it by its title bar). */
export const TollPanel = () => {
    const [open, setOpen] = useState(false);
    const close = () => {
        rememberPosition();
        setOpen(false);
    };

    const plazas = useValue(plazas$);
    const boothLanes = useValue(boothLanes$);
    const revenue = useValue(revenue$);
    const revenueCents = useValue(revenueCents$);
    const cars = useValue(cars$);
    const trucks = useValue(trucks$);
    const buses = useValue(buses$);
    const exempt = useValue(exempt$);
    const carRate = useValue(carRate$);
    const truckRate = useValue(truckRate$);
    const busRate = useValue(busRate$);
    const cashMph = useValue(cashMph$);
    const etcMph = useValue(etcMph$);
    const months = useValue(months$);
    const plazaList = useValue(plazaList$);

    return (
        <>
            <FloatingButton src={ICON} selected={open} tooltipLabel="Toll Plaza"
                onSelect={() => (open ? close() : setOpen(true))} />
            {open && (
                <Panel draggable initialPosition={lastPosition} className={styles.panel} header="Toll Plaza" onClose={close}>
                    <div className={styles.section}>
                        <div className={styles.heading}>Overview</div>
                        <Row label="Toll plazas" value={count(plazas)} />
                        <Row label="Booth lanes" value={count(boothLanes)} />
                        <Row label="Revenue collected" value={revenue} />
                    </div>
                    <div className={styles.section}>
                        <div className={styles.heading}>Vehicles charged and revenue, all plazas</div>
                        <MonthCompare stats={months}
                            allTime={{ cars, trucks, buses, exempt, cents: revenueCents }} />
                    </div>
                    <div className={styles.section}>
                        <div className={styles.heading}>Your toll plazas (click to go there)</div>
                        {plazaList.length === 0 && <div className={styles.note}>No plazas with roads placed yet.</div>}
                        {plazaList.map(p => (
                            <Button key={`${p.entity.index}:${p.entity.version}`} variant="flat"
                                className={styles.plazaLink} onSelect={() => goTo(p.entity)}>
                                {/* Whole strings: the game's UI drops the spaces between JSX text pieces. */}
                                <div className={styles.plazaLinkName}>
                                    <span className={styles.label}>{`${p.name} (${p.lanes} booths)`}</span>
                                    {p.outdated && <span className={styles.outdated}>Old layout: bulldoze and re-place</span>}
                                </div>
                                <span className={styles.value}>
                                    {`${money(p.thisMonth.cents)} · ${countOf(p.thisMonth.cars + p.thisMonth.trucks + p.thisMonth.buses)} vehicles`}
                                </span>
                            </Button>
                        ))}
                    </div>
                    <div className={styles.note}>Exempt: emergency and city service vehicles ride free. Reset a city's numbers in Options › Toll Plaza.</div>
                    <div className={styles.section}>
                        <div className={styles.heading}>Toll rates</div>
                        <RateRow label="Cars" id="car" cents={carRate} />
                        <RateRow label="Trucks" id="truck" cents={truckRate} />
                        <RateRow label="Buses" id="bus" cents={busRate} />
                    </div>
                    <div className={styles.section}>
                        <div className={styles.heading}>Lane speeds</div>
                        <SpeedRow label="Cash lanes" id="cash" mph={cashMph} />
                        <SpeedRow label="E-Toll lanes" id="etc" mph={etcMph} />
                    </div>
                </Panel>
            )}
        </>
    );
};
