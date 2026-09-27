import { bindValue, trigger, useValue } from "cs2/api";
import { Button, FloatingButton, Panel } from "cs2/ui";
import { useState } from "react";
import styles from "./toll-panel.module.scss";

// Values published by TollUISystem.cs (binding group "tollPlaza").
const group = "tollPlaza";
const plazas$ = bindValue<number>(group, "plazas", 0);
const boothLanes$ = bindValue<number>(group, "boothLanes", 0);
const revenue$ = bindValue<string>(group, "revenue", "$0.00");
const cars$ = bindValue<number>(group, "cars", 0);
const trucks$ = bindValue<number>(group, "trucks", 0);
const buses$ = bindValue<number>(group, "buses", 0);
const exempt$ = bindValue<number>(group, "exempt", 0);
const carRate$ = bindValue<number>(group, "carRate", 250);
const truckRate$ = bindValue<number>(group, "truckRate", 600);
const busRate$ = bindValue<number>(group, "busRate", 400);
const cashMph$ = bindValue<number>(group, "cashMph", 10);
const etcMph$ = bindValue<number>(group, "etcMph", 35);

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

/** Top-left toolbar button that opens the Toll Plaza control panel. */
export const TollPanel = () => {
    const [open, setOpen] = useState(false);

    const plazas = useValue(plazas$);
    const boothLanes = useValue(boothLanes$);
    const revenue = useValue(revenue$);
    const cars = useValue(cars$);
    const trucks = useValue(trucks$);
    const buses = useValue(buses$);
    const exempt = useValue(exempt$);
    const carRate = useValue(carRate$);
    const truckRate = useValue(truckRate$);
    const busRate = useValue(busRate$);
    const cashMph = useValue(cashMph$);
    const etcMph = useValue(etcMph$);

    return (
        <>
            <FloatingButton src={ICON} selected={open} tooltipLabel="Toll Plaza"
                onSelect={() => setOpen(!open)} />
            {open && (
                <Panel className={styles.panel} header="Toll Plaza" onClose={() => setOpen(false)}>
                    <div className={styles.section}>
                        <div className={styles.heading}>Overview</div>
                        <Row label="Toll plazas" value={count(plazas)} />
                        <Row label="Booth lanes" value={count(boothLanes)} />
                        <Row label="Revenue collected" value={revenue} />
                    </div>
                    <div className={styles.section}>
                        <div className={styles.heading}>Vehicles charged</div>
                        <Row label="Cars" value={count(cars)} />
                        <Row label="Trucks" value={count(trucks)} />
                        <Row label="Buses" value={count(buses)} />
                        <Row label="Exempt (emergency & service)" value={count(exempt)} />
                    </div>
                    <div className={styles.section}>
                        <div className={styles.heading}>Toll rates</div>
                        <RateRow label="Cars" id="car" cents={carRate} />
                        <RateRow label="Trucks" id="truck" cents={truckRate} />
                        <RateRow label="Buses" id="bus" cents={busRate} />
                    </div>
                    <div className={styles.section}>
                        <div className={styles.heading}>Lane speeds</div>
                        <Row label="Cash lanes" value={`${cashMph} mph`} />
                        <Row label="E-Toll lanes" value={`${etcMph} mph`} />
                    </div>
                </Panel>
            )}
        </>
    );
};
