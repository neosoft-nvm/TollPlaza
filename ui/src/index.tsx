import { ModRegistrar } from "cs2/modding";
import { TollPanel } from "mods/toll-panel";
import { registerPlazaSection } from "mods/plaza-section";

const register: ModRegistrar = (moduleRegistry) => {
    // Toolbar button (top left of the game screen) that opens the Toll Plaza control panel.
    moduleRegistry.append("GameTopLeft", TollPanel);
    // "Toll Plaza" section in the info panel when a plaza is clicked (monthly numbers, per booth).
    moduleRegistry.extend("game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx",
        "selectedInfoSectionComponents", registerPlazaSection);
};

export default register;
