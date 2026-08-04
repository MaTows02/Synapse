# Contribuer à Synapse

Merci de contribuer à Synapse.

1. Ouvrez d’abord une issue pour un changement important.
2. Créez une branche dédiée depuis `main`.
3. Gardez les opérations Windows réversibles et documentez leur méthode de vérification.
4. N’affichez jamais une compatibilité matérielle simulée : distinguez détection, lecture et contrôle.
5. Ajoutez ou mettez à jour les tests concernés.
6. Vérifiez `dotnet test .\Synapse.sln -c Release` avant la pull request.

Une pull request doit expliquer le problème, la solution, les risques, la procédure de restauration et la validation effectuée.
