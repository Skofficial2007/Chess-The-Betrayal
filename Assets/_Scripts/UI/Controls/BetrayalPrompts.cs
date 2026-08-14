namespace ChessTheBetrayal.UI.Controls
{
    /// <summary>
    /// The two questions the Betrayal rules are worth asking before answering for someone.
    ///
    /// Both are one tap away from something the rest of the match has to live with: the Act spends
    /// a right neither side gets back, and sparing the Betrayer hands a piece to the opponent for
    /// good. Neither is recoverable by playing on, and both sit on squares a finger can find by
    /// accident — a friendly piece looks exactly like an enemy one when you are moving quickly.
    ///
    /// Built once when this type is first touched, not per press, so a player who keeps changing
    /// their mind costs nothing.
    ///
    /// Both name their own buttons rather than leaving the panel's. The panel keeps whatever the
    /// last question set, so a question that renames the pair would otherwise leave the next one
    /// telling the player to press something that is no longer there — and the hint line at the
    /// bottom of both of these names the buttons out loud.
    /// </summary>
    public static class BetrayalPrompts
    {
        private const string ConfirmLabel = "Continue";
        private const string CancelLabel = "Back";
        private const string ButtonHint = "Press Continue to commit, or Back to cancel.";

        /// <summary>Turning on one of your own pieces, which can only ever happen once a match.</summary>
        public static readonly ConfirmationRequest Act = new ConfirmationRequest(
            MatchWarningMessage.Build(
                headline: "Are you sure you want to \ntrigger a Betrayal?",
                body: "This is a <color=#FFD700>once-per-match</color> global ability. \n"
                    + "Once used, neither player can betray again.\n\n"
                    + "You must have a <color=#FF9800>Retribution</color> move ready to execute the betrayer, otherwise they will permanently \n"
                    + "<color=#FF9800>defect</color> to the enemy team!",
                hint: ButtonHint),
            ConfirmLabel,
            CancelLabel);

        /// <summary>Declining the Retribution that is the only way to take the Betrayer back.</summary>
        public static readonly ConfirmationRequest SkipRetribution = new ConfirmationRequest(
            MatchWarningMessage.Build(
                headline: "Are you sure you want to<br>spare the betrayer?",
                body: "Skipping your <b><color=#FF9800>Retribution</color></b> move means<br>"
                    + "you are choosing not to execute them.\n\n"
                    + "The piece will immediately and<br>"
                    + "permanently <b><color=#FF9800>defect</color></b> to the enemy team!",
                hint: ButtonHint),
            ConfirmLabel,
            CancelLabel);
    }
}
