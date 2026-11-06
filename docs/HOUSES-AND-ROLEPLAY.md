# Houses, associates and a colonist's perspective

The implementation now separates a chosen social family from the recorded ancestry and physical household. This enables several play styles using the same systems, rather than assigning every house a fixed moral identity.

| Play style | Supported building blocks | Further depth still needed |
|---|---|---|
| Crime families | Associates, earned respect, aid/trading, private supplies, theft, rooted grievances, revenge recruitment, authority and accepted/denied petitions | Organized profit-motivated heists, protection rackets and criminal enterprises |
| Robinson / Lost in Space | A small household/family sharing reserves, caregiving, teaching, resource shortage and recovery | Specialized survival cooperation and expedition planning |
| Firefly / chosen crew | Admit unrelated people into a social house, retain separate ancestry, recognize trusted outside associates, delegate practical authority, allow departure and branches | Crew duties, shared missions and ship-specific integration |
| Rival dynasties | House identity and crests, branches, separate loyalties, earned standing, heads/stewards, admission and two-head union negotiations | Elections, legitimacy contests, autonomous succession disputes and political coups |

These are narrative supports, not claims of fully implemented media-specific campaigns.

## Social family and ancestry

A person's `PrimaryLineageId`, parent links and actual kinship remain recorded. An explicit social membership can select another existing family, select no house, or restore the original automatic rules. Routine observation does not overwrite an explicit choice. A newly recorded child whose recorded parents share one explicit social house inherits that social affiliation; disagreeing parents require a player choice.

Admission changes membership, not ownership of the previous family's communal goods. Explicit house union moves current members and communal ownership/possession to the receiving house, retaining ancestry, bonds and historical incident family IDs. In-flight requests involving changed members cancel without a fictional handover. Old grievances are not automatically forgiven. One social house still has one preferred home area; expanding or assigning that home remains a player decision, and actual household residence is separate.

A branch has its own founder, name and membership, a link to the parent house and inherited crest with a pattern variation. Its founder's ancestry is unchanged. Crests have eight colors, six marks and three patterns, edited on Overview. Six shared icon masks are cached; colors/patterns draw directly without per-lineage texture generation.

Known parent/adoptive parent links, siblings, avuncular relationships, first cousins and direct ancestors are excluded from mod-initiated romance. Parent-partner guardian relationships are excluded. Distant adult eligibility uses ancestor distance, not house membership or surname. In-game romance attempts additionally require biological age 18 and vanilla eligibility; the mod never raises a vanilla-rejected chance. Unknown/unrecorded pawns are handled conservatively; this is not a complete biological-ancestry certificate.

## Earned associates and authority

House contacts track respect separately for every person/house pair. Membership in another house does not prevent association. Actual gifts, repayments and sustained positive contact can earn standing; delivered household provisions earn recognition for real work. Positive credit is capped per day. Known wrongdoing reduces the victim house's respect once per incident; an unwitnessed theft does not automatically expose the thief.

- **Associate:** at least six respect over three service days.
- **Trusted associate:** at least twenty respect over ten service days.
- **Member:** belongs to the house. Membership alone does not grant administrative power.
- **Steward:** a member explicitly appointed by the head; can manage reserves, traditions and identity.
- **Head:** the recognized head; can sponsor admission, approve petitions and appoint/revoke stewards.

Associates can be invited to gatherings without meeting every host's ordinary friendship threshold. Association does not grant access to private stores or authority. A pawn can be respected by several houses and have conflicting loyalties.

For initial compatibility, an uninitialized house recognizes its living founder, or its oldest present nonjuvenile member when no founder is available. That incumbent is persisted: admitting an older person does not automatically usurp them. A vacant head position falls back when the incumbent is no longer a present member. This is an initial authority model, not elections, legitimacy simulation or a complete succession system.

## Acting as a colonist

The first opening chooses a selected colonist or a present nonjuvenile colonist. **Acting as** lets the player choose another representative or explicitly use **Colony overseer** for unrestricted setup/management. This is a roleplay lens, not a security boundary against debug tools or vanilla colony controls.

When Perspective Shift is active, its current avatar becomes the acting pawn automatically; selecting or ordering another pawn does not silently change the Family Life perspective. Director mode retains the normal selected-pawn behavior. Autonomous house decisions do not take control of the avatar. NPCs may walk over and speak to the avatar, but Family Life does not replace the avatar's current job with a listener job; the approach simply fails if the player moves away. Shared activities the player chose can still produce organic moments. **Spend time with...**, **Try to make peace with...**, and evidence-backed **Discuss what happened with...** actions let the avatar deliberately engage without choosing the outcome. This is a soft reflection integration, so Perspective Shift is never a required dependency.

As an ordinary member or outsider:

- Seek admission or recognition through an actual conversation with the house's head.
- Ask another house for food aid, or offer your house's available food. The actual supply job, reserves, storage and donor response still govern handover.
- Withdraw yourself or found your own branch without rewriting ancestry.
- Build respect through completed actions and positive relationships.

As a head:

- Hear pending petitions and accept or decline those actually delivered to you.
- Invite someone to join; the NPC's trust, respect and existing attachments affect willingness.
- Negotiate union with another head. Both houses' current heads must agree through a conversation; there is no instantaneous unilateral annexation in colonist mode.
- Appoint or revoke stewards. Delegated authority is lost when a member moves to another house.

Petitions persist, have a three-day expiry, and cannot be approved before delivery. A new head must hear a pending request again. AI heads use current trust, respect and support thresholds; player heads decide through the panel. No automatic mass recruitment or political takeover was added.

Colonist perspective filters incident/plot records to that pawn's knowledge, limits private aims and relationship inspection to the acting pawn, and disables unrestricted record export. Authority alone does not reveal a secret plot. It is not a complete fog-of-war model for all of RimWorld's interfaces or every public family statistic.

## Agency, unequal treatment and repair

Connection and recognition modestly bias otherwise eligible social choices. Independence can motivate protected personal time, with three completed sessions on separate days fulfilling that need for breathing room; it does not force a house split. Interruptions do not count as completed sessions.

The attention ledger records actual parent-child one-to-one time over seven days. Medical care, meals and group gatherings do not count as favoritism evidence. The UI describes unequal **recorded attention**, never asserts deliberate intent, and does not create anger merely from missing observations.

Quality-time gains are capped per directed pair/day. Runtime social-event quality credit matches conversation duration and is zero when the underlying shared activity already recorded time. Third-party, promise and family-tie conversation shifts use the gradual pace. Large gatherings sample bounded pairs and distribute credit rather than expanding everyone against everyone.

Ceremony scenes are tied to the ceremony request rather than merely to whoever is standing nearby. Wedding scenes know both spouses and both houses; funeral blame and recollection must concern the person being mourned; reunion welcomes require an actual returning visitor. Ordinary scenes require both participants to belong to the relevant house tree (including branches), be immediate kin of a subject, or be a recorded visitor. Explicit host/outsider scenes are the narrow exception. Scenes require actual proximity and sight at the gathering, and adult-responsibility scenes cannot cast children into adult roles. Attendance outcomes count only invitees who were physically eligible to attend that map.

Known incidents can be discussed in physical reconciliation conversations. Accepted repair steps require separate days and informed participants; a family's authorized representative must finalize a family-level resolution. Complete matching restitution and an explicit authorized forgiveness remain separate ways out. A successful ordinary conversation does not instantly erase a severe rift in gradual mode.

## Household economy

Zone has saved food/fuel/medicine targets, emergency sharing and an opt-in public barter-fund policy. The public policy preserves a 100-silver reserve; actual pickup rechecks availability. Private food can be shared during starvation, and medicine during emergency tending, when the holder permits it; player forbiddance and movement limits remain intact.

Borrowers with surplus can offer repayment for collection by an available creditor. Only a physically completed matching transfer repays the debt; attempts do not. Barter funds are physically carried into inventory before use. Stolen goods retain their original owner even through a later gift. Disabling supply autonomy does not allow differently owned stacks to merge and erase identity.

## Live acceptance required

Before release, test UI layout/scrolling, every new job and interruption point, petitions across save/reload and head changes, house unions and branch births, role permissions and secret-plot visibility, three-house stolen-goods returns, emergency treatment, public cash reserve races and post-interruption recovery. Repeat on both supported versions and profile actual 3× TPS. Compiling and domain tests do not execute these game behaviors.
