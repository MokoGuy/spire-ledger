using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace SpireLedger;

// The mod's own UI strings, in the game's current language (French or English; English otherwise).
// Card, relic, power and badge names come from the game itself and are already localized.
internal static class Loc
{
    private static readonly Dictionary<string, (string En, string Fr)> Strings = new()
    {
        ["dealt"] = ("DEALT", "INFLIGÉS"),
        ["taken"] = ("TAKEN", "SUBIS"),
        ["healed"] = ("HEALED", "SOINS"),
        ["block"] = ("BLOCK", "ARMURE"),
        ["fight"] = ("FIGHT", "COMBAT"),
        ["heal"] = ("HEAL", "SOINS"),
        ["total"] = ("TOTAL", "TOTAL"),
        ["run_recap"] = ("Run Recap", "Récap de la partie"),
        ["combat_summary"] = ("Combat Summary", "Résumé du combat"),
        ["combat_stats"] = ("Combat Stats", "Stats du combat"),
        ["to_close"] = ("{0} to close", "{0} pour fermer"),
        ["to_dismiss"] = ("{0} or ✕ to dismiss", "{0} ou ✕ pour fermer"),
        ["damage_round"] = ("Damage / Round", "Dégâts / tour"),
        ["damage_fight"] = ("Damage / Fight", "Dégâts / combat"),
        ["combat_log"] = ("Combat Log", "Journal du combat"),
        ["round"] = ("Round {0}", "Tour {0}"),
        ["fight_n"] = ("Fight {0}", "Combat {0}"),
        ["run_so_far"] = ("RUN SO FAR — {0} prior fight(s)", "PARTIE EN COURS — {0} combat(s) précédent(s)"),
        ["more"] = ("+ {0} more", "+ {0} de plus"),
        ["potions"] = ("Potions", "Potions"),
        ["you"] = ("You", "Vous"),
        ["log_healed"] = ("{0} healed  {1}", "{0} soigné  {1}"),
        ["log_used"] = ("{0} used{1}", "{0} utilisée{1}"),
        ["no_damage"] = ("No damage yet", "Aucun dégât pour l'instant"),
        ["dealt_n"] = ("Dealt {0}", "Infligés {0}"),
        ["taken_n"] = ("Taken {0}", "Subis {0}"),
        ["damage_title"] = ("Damage", "Dégâts"),
        ["out_word"] = (" Out", " Infligés"),
        ["in_word"] = ("In", "Subis"),
        ["per_round"] = (" / Round", " / tour"),
        ["incoming_forecast"] = ("Incoming Forecast", "Dégâts à venir"),
        ["left"] = ("left", "restants"),
        ["in_n"] = ("In {0}", "Subis {0}"),
        ["hp_lost_round"] = ("HP lost / round", "PV perdus / tour"),
        ["hidden_intent"] = ("Hidden intent  ?", "Intention cachée  ?"),
        // Party table (co-op)
        ["party"] = ("PARTY", "ÉQUIPE"),
        ["player"] = ("PLAYER", "JOUEUR"),
        ["effects"] = ("EFFECTS", "EFFETS"),
        ["block_given"] = ("GIVEN", "DONNÉE"),
        ["cards"] = ("CARDS", "CARTES"),
        ["energy"] = ("ENERGY", "ÉNERGIE"),
        ["dmg_per_energy"] = ("DMG/E", "DÉG./É"),
        ["biggest"] = ("BIGGEST HIT", "PLUS GROS COUP"),
        ["badges"] = ("Badges", "Badges"),
        ["no_badge"] = ("no badge", "aucun badge"),
        // Ledger screen
        ["ledger"] = ("Ledger", "Registre"),
        ["ledger_title"] = ("Spire Ledger — run history", "Spire Ledger — historique des parties"),
        ["no_runs"] = ("No run recorded yet. Finish a fight with the mod enabled.", "Aucune partie enregistrée. Terminez un combat avec le mod activé."),
        ["won"] = ("Victory", "Victoire"),
        ["lost"] = ("Defeat", "Défaite"),
        ["in_progress"] = ("In progress", "En cours"),
        ["floor"] = ("Floor {0}", "Étage {0}"),
        ["ascension"] = ("Ascension {0}", "Ascension {0}"),
        ["seed"] = ("Seed {0}", "Identifiant {0}"),
        ["fights"] = ("{0} fight(s)", "{0} combat(s)"),
        ["rounds_n"] = ("{0} rounds", "{0} tours"),
        ["records"] = ("Records", "Records"),
        ["record_dealt"] = ("Most damage in a run", "Plus de dégâts sur une partie"),
        ["record_hit"] = ("Biggest hit", "Plus gros coup"),
        ["record_fight"] = ("Most damage in one fight", "Plus de dégâts en un combat"),
        ["back"] = ("Back", "Retour"),
        ["mode_Standard"] = ("Standard", "Standard"),
        ["mode_Daily"] = ("Daily Climb", "Défi du jour"),
        ["mode_Custom"] = ("Custom", "Personnalisée"),
        ["page"] = ("{0} / {1}", "{0} / {1}"),
    };

    public static bool French
    {
        get
        {
            try { return LocManager.Instance?.Language == "fra"; }
            catch { return false; }
        }
    }

    public static string T(string key)
    {
        if (!Strings.TryGetValue(key, out var s)) return key;
        return French ? s.Fr : s.En;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);
}
