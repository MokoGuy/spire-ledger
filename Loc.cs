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
        ["debuffs"] = ("DEBUFFS", "DÉBUFFS"),
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
        // Progress import
        ["import_title"] = ("Import your progress?", "Importer votre progression ?"),
        ["import_body"] = ("With mods on, Slay the Spire 2 uses a separate save profile. Your normal profile has {0} unlocks (Daily Climb, characters, Ascension...), this one has {1}.\n\nCopy your normal progress into this profile? Your normal profile is not changed.",
                           "Avec des mods, Slay the Spire 2 utilise un profil de sauvegarde séparé. Votre profil normal a {0} déblocages (défi du jour, personnages, Ascension...), celui-ci {1}.\n\nCopier votre progression normale dans ce profil ? Votre profil normal n'est pas modifié."),
        ["import_body_short"] = ("With mods on, the game uses separate save profiles. Copy the unlocks (Daily Climb, characters, Ascension...) of a normal profile into a modded profile. The normal profile is not changed; the modded one is backed up first.",
                                 "Avec des mods, le jeu utilise des profils de sauvegarde séparés. Copiez les déblocages (défi du jour, personnages, Ascension...) d'un profil normal vers un profil moddé. Le profil normal n'est pas modifié ; le profil moddé est sauvegardé avant."),
        ["import_source"] = ("From (no mods)", "Depuis (sans mods)"),
        ["import_target"] = ("To (with mods)", "Vers (avec mods)"),
        ["profile_n"] = ("Profile {0}", "Profil {0}"),
        ["unlocks_n"] = ("{0} unlocks", "{0} déblocages"),
        ["profile_empty"] = ("empty", "vide"),
        ["active"] = ("active", "actif"),
        ["import_yes"] = ("Import", "Importer"),
        ["import_later"] = ("Later", "Plus tard"),
        ["import_never"] = ("Don't ask again", "Ne plus demander"),
        ["import_failed"] = ("The import failed: {0}", "L'import a échoué : {0}"),
        ["import_done_title"] = ("Progress imported", "Progression importée"),
        ["import_done_body"] = ("Restart the game so every unlock is taken into account.", "Redémarrez le jeu pour que tous les déblocages soient pris en compte."),
        ["forecast_on"] = ("Incoming-damage forecast shown", "Prévision des dégâts affichée"),
        ["forecast_off"] = ("Incoming-damage forecast hidden", "Prévision des dégâts masquée"),
        ["bars_on"] = ("Damage chart shown", "Graphique des dégâts affiché"),
        ["bars_off"] = ("Damage chart hidden", "Graphique des dégâts masqué"),
        ["party_hud_list"] = ("Party damage: next to the player list", "Dégâts de l'équipe : à côté de la liste des joueurs"),
        ["party_hud_characters"] = ("Party damage: under the characters", "Dégâts de l'équipe : sous les personnages"),
        ["party_hud_off"] = ("Party damage hidden", "Dégâts de l'équipe masqués"),
        ["ci_not_in_deck"] = ("Not in your deck", "Pas dans votre deck"),
        ["ci_in_deck"] = ("In your deck: {0}", "Dans votre deck : {0}"),
        ["ci_in_deck_up"] = ("In your deck: {0} ({1} upgraded)", "Dans votre deck : {0} (dont {1} améliorée(s))"),
        ["ci_played_run"] = ("Played {0}× this run", "Jouée {0} fois cette partie"),
        ["ci_hist_runs"] = ("{0} past runs, {1} won ({2}%)", "{0} parties passées, {1} gagnée(s) ({2} %)"),
        ["ci_hist_plays"] = ("played {0}×/run", "jouée {0} fois/partie"),
        ["ci_hist_none"] = ("Never in your past decks", "Jamais dans vos decks passés"),
        ["max_hp"] = ("max HP", "PV max"),
        ["version_mismatch"] = ("{0} has Spire Ledger {1}, you have {2}: update so you both see the same stats.",
                                "{0} a Spire Ledger {1}, vous {2} : mettez à jour pour voir les mêmes stats."),
        ["import_menu_button"] = ("Import progress", "Importer une progression"),
        ["import_button"] = ("Import my unmodded progress", "Importer ma progression sans mods"),
        ["import_nothing"] = ("This profile already has all your unlocks ({0}).", "Ce profil a déjà tous vos déblocages ({0})."),
        ["quit_game"] = ("Quit the game", "Quitter le jeu"),
        ["ok"] = ("OK", "OK"),
        ["cancel"] = ("Cancel", "Annuler"),
        ["all_runs"] = ("All runs ({0})", "Toutes les parties ({0})"),
        ["with_mate"] = ("With {0} ({1})", "Avec {0} ({1})"),
        ["mate_summary"] = ("With {0}: {1} run(s), {2} win(s) — average per run", "Avec {0} : {1} partie(s), {2} victoire(s) — moyenne par partie"),
        ["avg_dealt"] = ("DEALT", "INFLIGÉS"),
        ["avg_effects"] = ("EFFECTS", "EFFETS"),
        ["avg_given"] = ("GIVEN", "DONNÉE"),
        ["avg_taken"] = ("TAKEN", "SUBIS"),
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
