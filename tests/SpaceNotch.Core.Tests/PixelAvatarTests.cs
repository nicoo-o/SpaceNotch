using System;
using System.Linq;
using SpaceNotch.Core.Channel;
using SpaceNotch.Core.Motion;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Pixel, avatar des agents (ADR-029) : l'humeur selon l'état, et chaque humeur lisible.</summary>
public sealed class PixelAvatarTests
{
    private static readonly AgentMood[] Moods = Enum.GetValues<AgentMood>();

    [Theory]
    [InlineData(ChannelState.Working, false, AgentMood.Thinking)]
    [InlineData(ChannelState.Waiting, true, AgentMood.Asking)]
    [InlineData(ChannelState.Done, false, AgentMood.Done)]
    [InlineData(ChannelState.Error, false, AgentMood.Error)]
    public void L_humeur_suit_l_etat_de_l_agent(ChannelState state, bool asks, AgentMood expected)
        => Assert.Equal(expected, PixelAvatar.MoodOf(state, asks));

    [Fact]
    public void Une_attente_sans_question_se_montre_comme_une_erreur()
        // Claude Code attend une saisie dans le terminal : il faut y aller.
        => Assert.Equal(AgentMood.Error, PixelAvatar.MoodOf(ChannelState.Waiting, asks: false));

    [Fact]
    public void Chaque_humeur_a_son_signe_l_etat_se_lit_sans_la_couleur()
        => Assert.Equal(Moods.Length, Moods.Select(PixelAvatar.SignOf).Distinct().Count());

    [Fact]
    public void Chaque_humeur_a_ses_yeux()
    {
        AvatarPose thinking = PixelAvatar.Pose(AgentMood.Thinking, 0, animate: false);
        AvatarPose asking = PixelAvatar.Pose(AgentMood.Asking, 0, animate: false);
        AvatarPose done = PixelAvatar.Pose(AgentMood.Done, 0, animate: false);
        AvatarPose error = PixelAvatar.Pose(AgentMood.Error, 0, animate: false);

        // Réfléchit : les yeux de Pixel au repos, le regard en haut.
        Assert.Equal(PixelGaze.Shape(PixelMood.Awake), thinking.Eye);
        Assert.True(thinking.LookY < 0);

        // Demande : yeux ronds.
        Assert.Equal(1, asking.Eye.Roundness);
        Assert.Equal(asking.Eye.Width, asking.Eye.Height);

        // Terminé : yeux rieurs ; erreur : yeux penchés, inquiets.
        Assert.True(done.Arcs);
        Assert.False(error.Arcs);
        Assert.NotEqual(0, error.Tilt);
        Assert.Equal(0, thinking.Tilt + asking.Tilt + done.Tilt);
    }

    [Fact]
    public void Sans_animation_la_pose_est_fixe_et_le_signe_plein()
    {
        foreach (AgentMood mood in Moods)
        {
            AvatarPose early = PixelAvatar.Pose(mood, 0.1, animate: false);
            AvatarPose late = PixelAvatar.Pose(mood, 7.3, animate: false);

            Assert.Equal(early, late);
            Assert.Equal(0, early.Hop);
            Assert.Equal(0, early.Shake);
            Assert.Equal(1, early.SignOpacity);
        }

        Assert.Equal(3, PixelAvatar.Pose(AgentMood.Thinking, 0, animate: false).SignStep);
    }

    [Fact]
    public void Les_yeux_restent_dans_l_avatar()
    {
        foreach (AgentMood mood in Moods)
        {
            for (double t = 0; t < 12; t += 0.013)
            {
                AvatarPose pose = PixelAvatar.Pose(mood, t);

                Assert.InRange(pose.LookX, -PixelGaze.MaxLookX, PixelGaze.MaxLookX);
                Assert.InRange(pose.LookY, -PixelGaze.MaxLookY, PixelGaze.MaxLookY);
                Assert.InRange(pose.Eye.Height + Math.Abs(pose.Hop), 0, PixelAvatar.Height);
                Assert.InRange(pose.Shake, -1.5, 1.5);
                Assert.InRange(pose.SignOpacity, 0.3, 1);
            }
        }
    }

    [Fact]
    public void Le_regard_qui_cherche_va_et_vient_et_les_points_avancent()
    {
        double[] looks = [.. Enumerable.Range(0, 32).Select(i => PixelAvatar.Pose(AgentMood.Thinking, i * 0.1).LookX)];
        int[] dots = [.. Enumerable.Range(0, 12).Select(i => PixelAvatar.Pose(AgentMood.Thinking, i * 0.4).SignStep)];

        Assert.True(looks.Max() > 2 && looks.Min() < -2);
        Assert.Equal([1, 2, 3], dots.Distinct().Order());
    }

    [Fact]
    public void La_fete_est_courte_puis_il_reste_content()
    {
        Assert.True(PixelAvatar.Pose(AgentMood.Done, 0.2).Hop < 0);
        Assert.Equal(0, PixelAvatar.Pose(AgentMood.Done, 1.5).Hop);
        Assert.True(PixelAvatar.Pose(AgentMood.Done, 30).Arcs);
    }

    [Fact]
    public void Il_secoue_la_tete_puis_s_arrete_et_recommence()
    {
        Assert.NotEqual(0, PixelAvatar.Pose(AgentMood.Error, 0.05).Shake);
        Assert.Equal(0, PixelAvatar.Pose(AgentMood.Error, 1.5).Shake);
        Assert.NotEqual(0, PixelAvatar.Pose(AgentMood.Error, 2.65).Shake);
    }

    [Fact]
    public void Le_point_d_interrogation_clignote()
    {
        Assert.Equal(1, PixelAvatar.Pose(AgentMood.Asking, 0.1).SignOpacity);
        Assert.True(PixelAvatar.Pose(AgentMood.Asking, 1.0).SignOpacity < 1);
    }

    [Fact]
    public void La_largeur_garde_la_place_des_yeux_et_du_signe()
    {
        Assert.Equal(PixelAvatar.SignLeft + PixelAvatar.SignWidth, PixelAvatar.Width);
        Assert.True(PixelAvatar.RightEyeX - PixelAvatar.LeftEyeX == PixelAvatar.EyeWidth + PixelAvatar.EyeGap);
    }

    [Fact]
    public void Un_oeil_ne_sort_jamais_de_l_avatar_ni_ne_touche_le_signe()
    {
        // La pastille rogne l'avatar : un œil rond, ou qui regarde de côté, a été coupé (capture du 2026-10-10).
        foreach (AgentMood mood in Moods)
        {
            for (double t = 0; t < 12; t += 0.013)
            {
                AvatarPose pose = PixelAvatar.Pose(mood, t);
                double half = pose.Eye.Width / 2;

                // Un œil penché occupe un peu plus que sa forme : sa boîte englobante.
                double tilt = Math.Abs(pose.Tilt) * Math.PI / 180;
                double halfWidth = ((pose.Eye.Width * Math.Cos(tilt)) + (pose.Eye.Height * Math.Sin(tilt))) / 2;
                double halfHeight = ((pose.Eye.Width * Math.Sin(tilt)) + (pose.Eye.Height * Math.Cos(tilt))) / 2;
                double top = PixelAvatar.EyeY + pose.LookY + pose.Hop - halfHeight;
                double bottom = PixelAvatar.EyeY + pose.LookY + pose.Hop + halfHeight;

                Assert.True(PixelAvatar.LeftEyeX - Math.Max(half, halfWidth) + pose.LookX + Math.Min(0, pose.Shake) >= 0, $"{mood} à {t:0.00} s : œil gauche coupé");
                Assert.True(PixelAvatar.RightEyeX + Math.Max(half, halfWidth) + pose.LookX <= PixelAvatar.SignLeft, $"{mood} à {t:0.00} s : œil droit sur le signe");
                Assert.True(top >= 0 && bottom <= PixelAvatar.Height, $"{mood} à {t:0.00} s : œil coupé en haut ou en bas");
            }
        }
    }
}
