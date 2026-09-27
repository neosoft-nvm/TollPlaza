import { ModRegistrar } from "cs2/modding";
import { TollPanel } from "mods/toll-panel";

const register: ModRegistrar = (moduleRegistry) => {
    // Toolbar button (top left of the game screen) that opens the Toll Plaza control panel.
    moduleRegistry.append("GameTopLeft", TollPanel);
};

export default register;
